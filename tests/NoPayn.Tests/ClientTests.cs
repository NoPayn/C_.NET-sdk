using System.Net;
using System.Text;
using System.Text.Json;
using NoPayn.Exceptions;
using NoPayn.Models;
using Xunit;

namespace NoPayn.Tests;

public class ClientTests : IDisposable
{
    private const string ApiKey = "test-key-abc";
    private const string MerchantId = "merchant-123";
    private const string BaseUrl = "https://api.test.nopayn.co.uk";

    private static readonly NoPaynConfig Config = new(ApiKey, MerchantId, BaseUrl);

    private static readonly string OrderJson = JsonSerializer.Serialize(new
    {
        id = "order-uuid-001",
        amount = 1295,
        currency = "EUR",
        status = "new",
        description = "Test order",
        merchant_order_id = "SHOP-001",
        return_url = "https://shop.test/success",
        failure_url = "https://shop.test/failure",
        order_url = "https://api.nopayn.co.uk/pay/order-uuid-001/",
        created = "2026-01-15T10:00:00+00:00",
        modified = "2026-01-15T10:00:01+00:00",
        transactions = new[]
        {
            new
            {
                id = "txn-uuid-001",
                amount = 1295,
                currency = "EUR",
                payment_method = "credit-card",
                payment_url = "https://api.nopayn.co.uk/redirect/txn-uuid-001/to/payment/",
                status = "new",
                created = "2026-01-15T10:00:00+00:00",
                modified = "2026-01-15T10:00:01+00:00",
                expiration_period = "PT30M",
            }
        }
    });

    private static readonly string RefundJson = JsonSerializer.Serialize(new
    {
        id = "refund-uuid-001",
        amount = 500,
        status = "pending",
    });

    private static readonly string TransactionJson = JsonSerializer.Serialize(new
    {
        id = "txn-uuid-001",
        amount = 1295,
        currency = "EUR",
        payment_method = "credit-card",
        payment_url = "https://api.nopayn.co.uk/redirect/txn-uuid-001/to/payment/",
        status = "captured",
        created = "2026-01-15T10:00:00+00:00",
        modified = "2026-01-15T10:05:00+00:00",
        expiration_period = "PT30M",
    });

    private HttpRequestMessage? _capturedRequest;
    private string? _capturedRequestBody;

    private NoPaynClient CreateClient(HttpStatusCode status, string responseBody)
    {
        var handler = new MockHttpHandler(async request =>
        {
            _capturedRequest = request;
            if (request.Content is not null)
                _capturedRequestBody = await request.Content.ReadAsStringAsync();

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        });

        return new NoPaynClient(Config, new HttpClient(handler));
    }

    [Fact]
    public void Constructor_ThrowsOnMissingApiKey()
    {
        Assert.Throws<NoPaynException>(() => new NoPaynClient(new NoPaynConfig("", MerchantId)));
    }

    [Fact]
    public void Constructor_ThrowsOnMissingMerchantId()
    {
        Assert.Throws<NoPaynException>(() => new NoPaynClient(new NoPaynConfig(ApiKey, "")));
    }

    [Fact]
    public async Task CreateOrderAsync_SendsCorrectRequest()
    {
        using var client = CreateClient(HttpStatusCode.Created, OrderJson);

        var order = await client.CreateOrderAsync(new CreateOrderParams
        {
            Amount = 1295,
            Currency = "EUR",
            MerchantOrderId = "SHOP-001",
            Description = "Test order",
            ReturnUrl = "https://shop.test/success",
            FailureUrl = "https://shop.test/failure",
            Locale = "en-GB",
        });

        Assert.NotNull(_capturedRequest);
        Assert.Equal(HttpMethod.Post, _capturedRequest.Method);
        Assert.EndsWith("/v1/orders/", _capturedRequest.RequestUri!.AbsolutePath);
        Assert.StartsWith("Basic ", _capturedRequest.Headers.Authorization!.ToString());

        var expectedAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ApiKey}:"));
        Assert.Equal($"Basic {expectedAuth}", _capturedRequest.Headers.Authorization.ToString());

        Assert.NotNull(_capturedRequestBody);
        using var doc = JsonDocument.Parse(_capturedRequestBody);
        Assert.Equal(1295, doc.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("EUR", doc.RootElement.GetProperty("currency").GetString());
        Assert.Equal("SHOP-001", doc.RootElement.GetProperty("merchant_order_id").GetString());
        Assert.Equal("en-GB", doc.RootElement.GetProperty("locale").GetString());
    }

    [Fact]
    public async Task CreateOrderAsync_MapsResponse()
    {
        using var client = CreateClient(HttpStatusCode.Created, OrderJson);

        var order = await client.CreateOrderAsync(new CreateOrderParams
        {
            Amount = 1295,
            Currency = "EUR",
        });

        Assert.Equal("order-uuid-001", order.Id);
        Assert.Equal(1295, order.Amount);
        Assert.Equal("EUR", order.Currency);
        Assert.Equal("new", order.Status);
        Assert.Equal("Test order", order.Description);
        Assert.Equal("SHOP-001", order.MerchantOrderId);
        Assert.Equal("https://api.nopayn.co.uk/pay/order-uuid-001/", order.OrderUrl);
        Assert.Single(order.Transactions);

        var txn = order.Transactions[0];
        Assert.Equal("txn-uuid-001", txn.Id);
        Assert.Equal("credit-card", txn.PaymentMethod);
        Assert.Equal("https://api.nopayn.co.uk/redirect/txn-uuid-001/to/payment/", txn.PaymentUrl);
        Assert.Equal("PT30M", txn.ExpirationPeriod);
    }

    [Fact]
    public async Task CreateOrderAsync_OmitsNullFields()
    {
        using var client = CreateClient(HttpStatusCode.Created, OrderJson);

        await client.CreateOrderAsync(new CreateOrderParams
        {
            Amount = 100,
            Currency = "EUR",
        });

        Assert.NotNull(_capturedRequestBody);
        using var doc = JsonDocument.Parse(_capturedRequestBody);
        Assert.False(doc.RootElement.TryGetProperty("description", out _));
        Assert.False(doc.RootElement.TryGetProperty("merchant_order_id", out _));
        Assert.False(doc.RootElement.TryGetProperty("webhook_url", out _));
    }

    [Fact]
    public async Task GetOrderAsync_SendsCorrectRequest()
    {
        using var client = CreateClient(HttpStatusCode.OK, OrderJson);

        var order = await client.GetOrderAsync("order-uuid-001");

        Assert.NotNull(_capturedRequest);
        Assert.Equal(HttpMethod.Get, _capturedRequest.Method);
        Assert.EndsWith("/v1/orders/order-uuid-001/", _capturedRequest.RequestUri!.AbsolutePath);
        Assert.Equal("order-uuid-001", order.Id);
    }

    [Fact]
    public async Task CreateRefundAsync_SendsCorrectRequest()
    {
        using var client = CreateClient(HttpStatusCode.Created, RefundJson);

        var refund = await client.CreateRefundAsync("order-uuid-001", 500, "Customer return");

        Assert.NotNull(_capturedRequest);
        Assert.Equal(HttpMethod.Post, _capturedRequest.Method);
        Assert.EndsWith("/v1/orders/order-uuid-001/refunds/", _capturedRequest.RequestUri!.AbsolutePath);

        Assert.NotNull(_capturedRequestBody);
        using var doc = JsonDocument.Parse(_capturedRequestBody);
        Assert.Equal(500, doc.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("Customer return", doc.RootElement.GetProperty("description").GetString());

        Assert.Equal("refund-uuid-001", refund.Id);
        Assert.Equal(500, refund.Amount);
        Assert.Equal("pending", refund.Status);
    }

    [Fact]
    public async Task GeneratePaymentUrlAsync_ReturnsSignature()
    {
        using var client = CreateClient(HttpStatusCode.Created, OrderJson);

        var result = await client.GeneratePaymentUrlAsync(new CreateOrderParams
        {
            Amount = 1295,
            Currency = "EUR",
        });

        Assert.Equal("order-uuid-001", result.OrderId);
        Assert.Equal("https://api.nopayn.co.uk/pay/order-uuid-001/", result.OrderUrl);
        Assert.Equal("https://api.nopayn.co.uk/redirect/txn-uuid-001/to/payment/", result.PaymentUrl);
        Assert.NotEmpty(result.Signature);
        Assert.Equal(64, result.Signature.Length);

        Assert.True(client.VerifySignature(1295, "EUR", "order-uuid-001", result.Signature));
    }

    [Fact]
    public async Task RequestAsync_ThrowsApiExceptionOnError()
    {
        var errorJson = """{"error":{"value":"Invalid API key"}}""";
        using var client = CreateClient(HttpStatusCode.Unauthorized, errorJson);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => client.GetOrderAsync("bad-id"));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("Invalid API key", ex.Message);
        Assert.NotNull(ex.ErrorBody);
    }

    [Fact]
    public async Task RequestAsync_ExtractsDetailErrorField()
    {
        var errorJson = """{"detail":"Not found"}""";
        using var client = CreateClient(HttpStatusCode.NotFound, errorJson);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => client.GetOrderAsync("missing"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Contains("Not found", ex.Message);
    }

    [Fact]
    public void GenerateSignature_UsesApiKeyAsSecret()
    {
        using var client = new NoPaynClient(Config, new HttpClient(new MockHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var sig = client.GenerateSignature(1295, "EUR", "order-1");
        var expected = NoPaynSignature.Generate(ApiKey, 1295, "EUR", "order-1");

        Assert.Equal(expected, sig);
    }

    [Fact]
    public void VerifySignature_ValidatesCorrectly()
    {
        using var client = new NoPaynClient(Config, new HttpClient(new MockHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var sig = client.GenerateSignature(1295, "EUR", "order-1");
        Assert.True(client.VerifySignature(1295, "EUR", "order-1", sig));
        Assert.False(client.VerifySignature(9999, "EUR", "order-1", sig));
    }

    [Fact]
    public async Task CaptureTransactionAsync_SendsCorrectRequest()
    {
        using var client = CreateClient(HttpStatusCode.OK, TransactionJson);

        var txn = await client.CaptureTransactionAsync("order-uuid-001", "txn-uuid-001");

        Assert.NotNull(_capturedRequest);
        Assert.Equal(HttpMethod.Post, _capturedRequest.Method);
        Assert.EndsWith(
            "/v1/orders/order-uuid-001/transactions/txn-uuid-001/captures/",
            _capturedRequest.RequestUri!.AbsolutePath);
        Assert.Null(_capturedRequestBody);

        Assert.Equal("txn-uuid-001", txn.Id);
        Assert.Equal(1295, txn.Amount);
        Assert.Equal("EUR", txn.Currency);
        Assert.Equal("captured", txn.Status);
    }

    [Fact]
    public async Task VoidTransactionAsync_SendsCorrectRequest()
    {
        var voidedTxnJson = JsonSerializer.Serialize(new
        {
            id = "txn-uuid-001",
            amount = 1295,
            currency = "EUR",
            payment_method = "credit-card",
            status = "voided",
            created = "2026-01-15T10:00:00+00:00",
            modified = "2026-01-15T10:05:00+00:00",
        });

        using var client = CreateClient(HttpStatusCode.OK, voidedTxnJson);

        var txn = await client.VoidTransactionAsync("order-uuid-001", "txn-uuid-001", 1295, "Customer cancelled");

        Assert.NotNull(_capturedRequest);
        Assert.Equal(HttpMethod.Post, _capturedRequest.Method);
        Assert.EndsWith(
            "/v1/orders/order-uuid-001/transactions/txn-uuid-001/voids/amount/",
            _capturedRequest.RequestUri!.AbsolutePath);

        Assert.NotNull(_capturedRequestBody);
        using var doc = JsonDocument.Parse(_capturedRequestBody);
        Assert.Equal(1295, doc.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("Customer cancelled", doc.RootElement.GetProperty("description").GetString());

        Assert.Equal("txn-uuid-001", txn.Id);
        Assert.Equal("voided", txn.Status);
    }

    [Fact]
    public async Task VoidTransactionAsync_OmitsNullDescription()
    {
        using var client = CreateClient(HttpStatusCode.OK, TransactionJson);

        await client.VoidTransactionAsync("order-uuid-001", "txn-uuid-001", 500);

        Assert.NotNull(_capturedRequestBody);
        using var doc = JsonDocument.Parse(_capturedRequestBody);
        Assert.Equal(500, doc.RootElement.GetProperty("amount").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task CreateOrderAsync_WithOrderLines_SendsCorrectRequest()
    {
        using var client = CreateClient(HttpStatusCode.Created, OrderJson);

        var order = await client.CreateOrderAsync(new CreateOrderParams
        {
            Amount = 1295,
            Currency = "EUR",
            OrderLines =
            [
                new OrderLine
                {
                    Type = "physical",
                    Name = "Widget",
                    Quantity = 2,
                    Amount = 1000,
                    Currency = "EUR",
                    VatPercentage = 19,
                    MerchantOrderLineId = "LINE-001",
                },
                new OrderLine
                {
                    Type = "shipping_fee",
                    Name = "Standard Shipping",
                    Quantity = 1,
                    Amount = 295,
                    Currency = "EUR",
                },
            ],
            Customer = new Dictionary<string, string>
            {
                ["email"] = "test@example.com",
                ["name"] = "John Doe",
            },
        });

        Assert.NotNull(_capturedRequestBody);
        using var doc = JsonDocument.Parse(_capturedRequestBody);

        var lines = doc.RootElement.GetProperty("order_lines");
        Assert.Equal(2, lines.GetArrayLength());

        var line1 = lines[0];
        Assert.Equal("physical", line1.GetProperty("type").GetString());
        Assert.Equal("Widget", line1.GetProperty("name").GetString());
        Assert.Equal(2, line1.GetProperty("quantity").GetInt32());
        Assert.Equal(1000, line1.GetProperty("amount").GetInt32());
        Assert.Equal("EUR", line1.GetProperty("currency").GetString());
        Assert.Equal(19, line1.GetProperty("vat_percentage").GetInt32());
        Assert.Equal("LINE-001", line1.GetProperty("merchant_order_line_id").GetString());

        var line2 = lines[1];
        Assert.Equal("shipping_fee", line2.GetProperty("type").GetString());
        Assert.Equal(295, line2.GetProperty("amount").GetInt32());
        Assert.False(line2.TryGetProperty("vat_percentage", out _));
        Assert.False(line2.TryGetProperty("merchant_order_line_id", out _));

        var customer = doc.RootElement.GetProperty("customer");
        Assert.Equal("test@example.com", customer.GetProperty("email").GetString());
        Assert.Equal("John Doe", customer.GetProperty("name").GetString());

        Assert.Equal("order-uuid-001", order.Id);
    }

    [Fact]
    public void OrderLine_Serialization_UsesSnakeCase()
    {
        var line = new OrderLine
        {
            Type = "discount",
            Name = "10% off",
            Quantity = 1,
            Amount = -100,
            Currency = "EUR",
            VatPercentage = 19,
            MerchantOrderLineId = "DISC-001",
        };

        var json = JsonSerializer.Serialize(line, NoPaynClient.JsonOptions);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("discount", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("10% off", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("quantity").GetInt32());
        Assert.Equal(-100, doc.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("EUR", doc.RootElement.GetProperty("currency").GetString());
        Assert.Equal(19, doc.RootElement.GetProperty("vat_percentage").GetInt32());
        Assert.Equal("DISC-001", doc.RootElement.GetProperty("merchant_order_line_id").GetString());
    }

    [Fact]
    public void OrderLine_Serialization_OmitsNullOptionalFields()
    {
        var line = new OrderLine
        {
            Type = "physical",
            Name = "Widget",
            Quantity = 1,
            Amount = 500,
            Currency = "EUR",
        };

        var json = JsonSerializer.Serialize(line, NoPaynClient.JsonOptions);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("vat_percentage", out _));
        Assert.False(doc.RootElement.TryGetProperty("merchant_order_line_id", out _));
    }

    public void Dispose() => _capturedRequest?.Dispose();
}

internal class MockHttpHandler(
    Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
}
