namespace NoPayn.Models;

public record Refund
{
    public string Id { get; init; } = "";
    public int Amount { get; init; }
    public string Status { get; init; } = "";
}
