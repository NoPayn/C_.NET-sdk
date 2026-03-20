namespace NoPayn.Models;

public record WebhookPayload
{
    public string Event { get; init; } = "";
    public string OrderId { get; init; } = "";
    public string? ProjectId { get; init; }
}
