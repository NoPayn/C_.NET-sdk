namespace NoPayn.Models;

public record Transaction
{
    public string Id { get; init; } = "";
    public int Amount { get; init; }
    public string Currency { get; init; } = "";
    public string? PaymentMethod { get; init; }
    public string? PaymentUrl { get; init; }
    public string Status { get; init; } = "";
    public string Created { get; init; } = "";
    public string Modified { get; init; } = "";
    public string? ExpirationPeriod { get; init; }
}
