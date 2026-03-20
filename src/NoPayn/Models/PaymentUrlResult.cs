namespace NoPayn.Models;

public record PaymentUrlResult(
    string OrderId,
    string OrderUrl,
    string? PaymentUrl,
    string Signature,
    Order Order
);
