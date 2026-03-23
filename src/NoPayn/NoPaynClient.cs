using System.Collections.Frozen;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoPayn.Exceptions;
using NoPayn.Models;

namespace NoPayn;

/// <summary>
/// Client for the NoPayn Payment Gateway API.
/// Handles order creation, HPP redirects, HMAC signing, and webhook verification.
/// </summary>
public sealed class NoPaynClient : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly FrozenSet<string> FinalStatuses =
        FrozenSet.ToFrozenSet(["completed", "cancelled", "expired", "error"]);

    private readonly string _apiKey;
    private readonly string _merchantId;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public NoPaynClient(NoPaynConfig config) : this(config, null) { }

    public NoPaynClient(NoPaynConfig config, HttpClient? httpClient)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrEmpty(config.ApiKey))
            throw new NoPaynException("ApiKey is required");
        if (string.IsNullOrEmpty(config.MerchantId))
            throw new NoPaynException("MerchantId is required");

        _apiKey = config.ApiKey;
        _merchantId = config.MerchantId;
        _baseUrl = config.BaseUrl.TrimEnd('/');

        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    // ── Order API ──────────────────────────────────────────────────────────────

    /// <summary>Create an order via <c>POST /v1/orders/</c>.</summary>
    public async Task<Order> CreateOrderAsync(CreateOrderParams orderParams)
    {
        var json = JsonSerializer.Serialize(orderParams, JsonOptions);
        return await RequestAsync<Order>(HttpMethod.Post, "/v1/orders/", json).ConfigureAwait(false);
    }

    /// <summary>Fetch an existing order via <c>GET /v1/orders/{id}/</c>.</summary>
    public async Task<Order> GetOrderAsync(string orderId)
    {
        return await RequestAsync<Order>(
            HttpMethod.Get,
            $"/v1/orders/{Uri.EscapeDataString(orderId)}/"
        ).ConfigureAwait(false);
    }

    /// <summary>Issue a full or partial refund via <c>POST /v1/orders/{id}/refunds/</c>.</summary>
    public async Task<Refund> CreateRefundAsync(string orderId, int amount, string? description = null)
    {
        var body = new RefundRequest { Amount = amount, Description = description };
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return await RequestAsync<Refund>(
            HttpMethod.Post,
            $"/v1/orders/{Uri.EscapeDataString(orderId)}/refunds/",
            json
        ).ConfigureAwait(false);
    }

    // ── Transaction API ────────────────────────────────────────────────────────

    /// <summary>Capture a transaction via <c>POST /v1/orders/{orderId}/transactions/{transactionId}/captures/</c>.</summary>
    public async Task<Transaction> CaptureTransactionAsync(string orderId, string transactionId)
    {
        return await RequestAsync<Transaction>(
            HttpMethod.Post,
            $"/v1/orders/{Uri.EscapeDataString(orderId)}/transactions/{Uri.EscapeDataString(transactionId)}/captures/"
        ).ConfigureAwait(false);
    }

    /// <summary>Void a transaction via <c>POST /v1/orders/{orderId}/transactions/{transactionId}/voids/amount/</c>.</summary>
    public async Task<Transaction> VoidTransactionAsync(string orderId, string transactionId, int amount, string? description = null)
    {
        var body = new VoidRequest { Amount = amount, Description = description };
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return await RequestAsync<Transaction>(
            HttpMethod.Post,
            $"/v1/orders/{Uri.EscapeDataString(orderId)}/transactions/{Uri.EscapeDataString(transactionId)}/voids/amount/",
            json
        ).ConfigureAwait(false);
    }

    // ── HPP Redirect ───────────────────────────────────────────────────────────

    /// <summary>
    /// Create an order and return the HPP redirect URL with an HMAC signature.
    /// The signature covers <c>amount:currency:orderId</c> so the merchant can
    /// verify that return/callback parameters haven't been tampered with.
    /// </summary>
    public async Task<PaymentUrlResult> GeneratePaymentUrlAsync(CreateOrderParams orderParams)
    {
        var order = await CreateOrderAsync(orderParams).ConfigureAwait(false);

        var signature = NoPaynSignature.Generate(
            _apiKey, orderParams.Amount, orderParams.Currency, order.Id);

        return new PaymentUrlResult(
            OrderId: order.Id,
            OrderUrl: order.OrderUrl!,
            PaymentUrl: order.Transactions.Count > 0 ? order.Transactions[0].PaymentUrl : null,
            Signature: signature,
            Order: order
        );
    }

    // ── HMAC Signature Utilities ───────────────────────────────────────────────

    /// <summary>Generate an HMAC-SHA256 hex signature for the given payment parameters.</summary>
    public string GenerateSignature(int amount, string currency, string orderId) =>
        NoPaynSignature.Generate(_apiKey, amount, currency, orderId);

    /// <summary>Constant-time verification of an HMAC-SHA256 signature.</summary>
    public bool VerifySignature(int amount, string currency, string orderId, string signature) =>
        NoPaynSignature.Verify(_apiKey, amount, currency, orderId, signature);

    // ── Webhook Handling ───────────────────────────────────────────────────────

    /// <summary>Parse a raw webhook body into a typed payload. Never trust the payload for status.</summary>
    public WebhookPayload ParseWebhookBody(string rawBody)
    {
        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            throw new WebhookException("Invalid JSON in webhook body");
        }

        if (payload is null || string.IsNullOrEmpty(payload.OrderId))
            throw new WebhookException("Missing order_id in webhook payload");

        return payload;
    }

    /// <summary>
    /// Parse the webhook body, then call the API to verify the actual order status.
    /// Returns the verified order and whether it has reached a final status.
    /// </summary>
    public async Task<VerifiedWebhook> VerifyWebhookAsync(string rawBody)
    {
        var payload = ParseWebhookBody(rawBody);
        var order = await GetOrderAsync(payload.OrderId).ConfigureAwait(false);

        return new VerifiedWebhook(
            OrderId: payload.OrderId,
            Order: order,
            IsFinal: FinalStatuses.Contains(order.Status)
        );
    }

    // ── Internal HTTP ──────────────────────────────────────────────────────────

    private async Task<T> RequestAsync<T>(HttpMethod method, string endpoint, string? jsonBody = null)
    {
        var url = $"{_baseUrl}{endpoint}";
        using var request = new HttpRequestMessage(method, url);

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_apiKey}:"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonBody is not null && method != HttpMethod.Get)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new NoPaynException($"Network error: {ex.Message}");
        }

        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = ExtractErrorMessage(text);
            throw new ApiException((int)response.StatusCode, errorMsg, text);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOptions)
                ?? throw new NoPaynException("Null response from API");
        }
        catch (JsonException)
        {
            throw new NoPaynException($"Invalid JSON response: {text[..Math.Min(text.Length, 200)]}");
        }
    }

    private static string ExtractErrorMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("value", out var val) && val.GetString() is { } v)
                    return v;
                if (errorObj.TryGetProperty("message", out var msg) && msg.GetString() is { } m)
                    return m;
            }

            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { } d)
                return d;
        }
        catch
        {
            // Fall through to default
        }

        return "Unknown error";
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private record RefundRequest
    {
        public int Amount { get; init; }
        public string? Description { get; init; }
    }

    private record VoidRequest
    {
        public int Amount { get; init; }
        public string? Description { get; init; }
    }
}
