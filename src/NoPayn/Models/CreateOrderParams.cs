namespace NoPayn.Models;

public record CreateOrderParams
{
    public required int Amount { get; init; }
    public required string Currency { get; init; }
    public string? MerchantOrderId { get; init; }
    public string? Description { get; init; }
    public string? ReturnUrl { get; init; }
    public string? FailureUrl { get; init; }
    public string? WebhookUrl { get; init; }
    public string? Locale { get; init; }
    public IReadOnlyList<string>? PaymentMethods { get; init; }
    public string? ExpirationPeriod { get; init; }
}
