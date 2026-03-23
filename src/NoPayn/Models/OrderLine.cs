namespace NoPayn.Models;

public record OrderLine
{
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public required int Amount { get; init; }
    public required string Currency { get; init; }
    public int? VatPercentage { get; init; }
    public string? MerchantOrderLineId { get; init; }
}
