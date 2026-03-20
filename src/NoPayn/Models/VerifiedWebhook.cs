namespace NoPayn.Models;

public record VerifiedWebhook(
    string OrderId,
    Order Order,
    bool IsFinal
);
