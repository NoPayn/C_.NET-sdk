using System.Net;
using System.Text;
using System.Text.Json;
using NoPayn.Exceptions;
using NoPayn.Models;
using Xunit;

namespace NoPayn.Tests;

public class WebhookTests
{
    private const string ApiKey = "test-key-abc";
    private const string MerchantId = "merchant-123";
    private const string BaseUrl = "https://api.test.nopayn.co.uk";

    private static readonly NoPaynConfig Config = new(ApiKey, MerchantId, BaseUrl);

    private static readonly string CompletedOrderJson = JsonSerializer.Serialize(new
    {
        id = "order-uuid-001",
        amount = 1295,
        currency = "EUR",
        status = "completed",
        created = "2026-01-15T10:00:00+00:00",
        modified = "2026-01-15T10:05:00+00:00",
        completed = "2026-01-15T10:05:00+00:00",
        transactions = Array.Empty<object>(),
    });

    private static readonly string ProcessingOrderJson = JsonSerializer.Serialize(new
    {
        id = "order-uuid-002",
        amount = 500,
        currency = "EUR",
        status = "processing",
        created = "2026-01-15T10:00:00+00:00",
        modified = "2026-01-15T10:01:00+00:00",
        transactions = Array.Empty<object>(),
    });

    private static NoPaynClient CreateClientWithResponse(string orderId, string responseJson)
    {
        var handler = new MockHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            }));

        return new NoPaynClient(Config, new HttpClient(handler));
    }

    [Fact]
    public void ParseWebhookBody_ParsesValidPayload()
    {
        using var client = CreateClientWithResponse("order-uuid-001", CompletedOrderJson);

        var payload = client.ParseWebhookBody(
            """{"event":"status_changed","order_id":"order-uuid-001","project_id":"proj-1"}""");

        Assert.Equal("status_changed", payload.Event);
        Assert.Equal("order-uuid-001", payload.OrderId);
        Assert.Equal("proj-1", payload.ProjectId);
    }

    [Fact]
    public void ParseWebhookBody_HandlesMinimalPayload()
    {
        using var client = CreateClientWithResponse("order-uuid-001", CompletedOrderJson);

        var payload = client.ParseWebhookBody(
            """{"event":"status_changed","order_id":"order-uuid-001"}""");

        Assert.Equal("status_changed", payload.Event);
        Assert.Equal("order-uuid-001", payload.OrderId);
        Assert.Null(payload.ProjectId);
    }

    [Fact]
    public void ParseWebhookBody_ThrowsOnInvalidJson()
    {
        using var client = CreateClientWithResponse("x", CompletedOrderJson);

        Assert.Throws<WebhookException>(() => client.ParseWebhookBody("not json {{{"));
    }

    [Fact]
    public void ParseWebhookBody_ThrowsOnMissingOrderId()
    {
        using var client = CreateClientWithResponse("x", CompletedOrderJson);

        Assert.Throws<WebhookException>(() =>
            client.ParseWebhookBody("""{"event":"status_changed"}"""));
    }

    [Fact]
    public void ParseWebhookBody_ThrowsOnEmptyOrderId()
    {
        using var client = CreateClientWithResponse("x", CompletedOrderJson);

        Assert.Throws<WebhookException>(() =>
            client.ParseWebhookBody("""{"event":"status_changed","order_id":""}"""));
    }

    [Fact]
    public async Task VerifyWebhookAsync_ReturnsFinalForCompletedOrder()
    {
        using var client = CreateClientWithResponse("order-uuid-001", CompletedOrderJson);

        var result = await client.VerifyWebhookAsync(
            """{"event":"status_changed","order_id":"order-uuid-001"}""");

        Assert.Equal("order-uuid-001", result.OrderId);
        Assert.Equal("completed", result.Order.Status);
        Assert.True(result.IsFinal);
    }

    [Fact]
    public async Task VerifyWebhookAsync_ReturnsNonFinalForProcessingOrder()
    {
        using var client = CreateClientWithResponse("order-uuid-002", ProcessingOrderJson);

        var result = await client.VerifyWebhookAsync(
            """{"event":"status_changed","order_id":"order-uuid-002"}""");

        Assert.Equal("order-uuid-002", result.OrderId);
        Assert.Equal("processing", result.Order.Status);
        Assert.False(result.IsFinal);
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("cancelled", true)]
    [InlineData("expired", true)]
    [InlineData("error", true)]
    [InlineData("new", false)]
    [InlineData("processing", false)]
    public async Task VerifyWebhookAsync_IsFinalForAllStatuses(string status, bool expectedFinal)
    {
        var orderJson = JsonSerializer.Serialize(new
        {
            id = "order-001",
            amount = 100,
            currency = "EUR",
            status,
            created = "2026-01-15T10:00:00+00:00",
            modified = "2026-01-15T10:00:00+00:00",
            transactions = Array.Empty<object>(),
        });

        using var client = CreateClientWithResponse("order-001", orderJson);

        var result = await client.VerifyWebhookAsync(
            """{"event":"status_changed","order_id":"order-001"}""");

        Assert.Equal(expectedFinal, result.IsFinal);
    }

    [Fact]
    public async Task VerifyWebhookAsync_ThrowsOnInvalidBody()
    {
        using var client = CreateClientWithResponse("x", CompletedOrderJson);

        await Assert.ThrowsAsync<WebhookException>(
            () => client.VerifyWebhookAsync("invalid json!!!"));
    }
}
