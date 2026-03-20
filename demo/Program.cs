using NoPayn;
using NoPayn.Exceptions;
using NoPayn.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var apiKey = Environment.GetEnvironmentVariable("NOPAYN_API_KEY") ?? "";
var merchantId = Environment.GetEnvironmentVariable("NOPAYN_MERCHANT_ID") ?? "";
var baseUrl = Environment.GetEnvironmentVariable("NOPAYN_BASE_URL") ?? "https://api.nopayn.co.uk";
var publicUrl = (Environment.GetEnvironmentVariable("PUBLIC_URL") ?? "http://localhost:3000").TrimEnd('/');

if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(merchantId))
{
    Console.Error.WriteLine("Set NOPAYN_API_KEY and NOPAYN_MERCHANT_ID environment variables");
    Environment.Exit(1);
}

using var nopayn = new NoPaynClient(new NoPaynConfig(apiKey, merchantId, baseUrl));

var viewsPath = Path.Combine(app.Environment.ContentRootPath, "Views");
var indexHtml = File.ReadAllText(Path.Combine(viewsPath, "Index.html"));
var successHtml = File.ReadAllText(Path.Combine(viewsPath, "Success.html"));
var failureHtml = File.ReadAllText(Path.Combine(viewsPath, "Failure.html"));

app.MapGet("/", () => Results.Content(indexHtml, "text/html"));

app.MapPost("/pay", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var amount = (int)Math.Round(double.Parse(form["amount"].FirstOrDefault() ?? "9.95") * 100);
        var currency = form["currency"].FirstOrDefault() ?? "EUR";
        var locale = form["locale"].FirstOrDefault() ?? "en-GB";
        var orderId = $"DEMO-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var result = await nopayn.GeneratePaymentUrlAsync(new CreateOrderParams
        {
            Amount = amount,
            Currency = currency,
            MerchantOrderId = orderId,
            Description = $"Demo order {orderId}",
            ReturnUrl = $"{publicUrl}/success?order_id={orderId}",
            FailureUrl = $"{publicUrl}/failure?order_id={orderId}",
            WebhookUrl = $"{publicUrl}/webhook",
            Locale = locale,
            ExpirationPeriod = "PT30M",
        });

        Console.WriteLine($"[PAY] Order {result.OrderId} created — signature: {result.Signature}");

        var redirectTo = result.PaymentUrl ?? result.OrderUrl;
        return Results.Redirect(redirectTo);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[PAY] Error: {ex}");
        var html = failureHtml
            .Replace("{{title}}", "Payment Error")
            .Replace("{{message}}", System.Net.WebUtility.HtmlEncode(ex.Message));
        return Results.Content(html, "text/html");
    }
});

app.MapGet("/success", (HttpContext ctx) =>
{
    var orderId = ctx.Request.Query["order_id"].FirstOrDefault() ?? "(unknown)";
    var html = successHtml.Replace("{{orderId}}", System.Net.WebUtility.HtmlEncode(orderId));
    return Results.Content(html, "text/html");
});

app.MapGet("/failure", (HttpContext ctx) =>
{
    var orderId = ctx.Request.Query["order_id"].FirstOrDefault() ?? "(unknown)";
    var html = failureHtml
        .Replace("{{title}}", "Payment Failed")
        .Replace("{{message}}", $"Order {System.Net.WebUtility.HtmlEncode(orderId)} was not completed.");
    return Results.Content(html, "text/html");
});

app.MapPost("/webhook", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        var verified = await nopayn.VerifyWebhookAsync(rawBody);

        Console.WriteLine(
            $"[WEBHOOK] Order {verified.OrderId} → {verified.Order.Status} (final: {verified.IsFinal})");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[WEBHOOK] Verification failed: {ex}");
    }

    return Results.Ok();
});

app.MapGet("/status/{orderId}", async (string orderId) =>
{
    try
    {
        var order = await nopayn.GetOrderAsync(orderId);
        return Results.Json(order, NoPaynClient.JsonOptions);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.Run();
