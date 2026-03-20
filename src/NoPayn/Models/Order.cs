namespace NoPayn.Models;

public record Order
{
    public string Id { get; init; } = "";
    public int Amount { get; init; }
    public string Currency { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Description { get; init; }
    public string? MerchantOrderId { get; init; }
    public string? ReturnUrl { get; init; }
    public string? FailureUrl { get; init; }
    public string? OrderUrl { get; init; }
    public string Created { get; init; } = "";
    public string Modified { get; init; } = "";
    public string? Completed { get; init; }
    public IReadOnlyList<Transaction> Transactions { get; init; } = [];
}
