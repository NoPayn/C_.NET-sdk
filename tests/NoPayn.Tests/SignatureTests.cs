using Xunit;

namespace NoPayn.Tests;

public class SignatureTests
{
    private const string Secret = "test-api-key-123";
    private const int Amount = 1295;
    private const string Currency = "EUR";
    private const string OrderId = "550e8400-e29b-41d4-a716-446655440000";

    [Fact]
    public void Generate_ProducesDeterministicOutput()
    {
        var sig1 = NoPaynSignature.Generate(Secret, Amount, Currency, OrderId);
        var sig2 = NoPaynSignature.Generate(Secret, Amount, Currency, OrderId);

        Assert.Equal(sig1, sig2);
        Assert.Equal(64, sig1.Length);
        Assert.Matches("^[0-9a-f]{64}$", sig1);
    }

    [Fact]
    public void Verify_RoundTrip()
    {
        var sig = NoPaynSignature.Generate(Secret, Amount, Currency, OrderId);
        Assert.True(NoPaynSignature.Verify(Secret, Amount, Currency, OrderId, sig));
    }

    [Fact]
    public void Verify_RejectsTamperedAmount()
    {
        var sig = NoPaynSignature.Generate(Secret, Amount, Currency, OrderId);
        Assert.False(NoPaynSignature.Verify(Secret, 9999, Currency, OrderId, sig));
    }

    [Fact]
    public void Verify_RejectsTamperedCurrency()
    {
        var sig = NoPaynSignature.Generate(Secret, Amount, Currency, OrderId);
        Assert.False(NoPaynSignature.Verify(Secret, Amount, "GBP", OrderId, sig));
    }

    [Fact]
    public void Verify_RejectsTamperedOrderId()
    {
        var sig = NoPaynSignature.Generate(Secret, Amount, Currency, OrderId);
        Assert.False(NoPaynSignature.Verify(Secret, Amount, Currency, "different-order", sig));
    }

    [Fact]
    public void Verify_RejectsWrongKey()
    {
        var sig = NoPaynSignature.Generate(Secret, Amount, Currency, OrderId);
        Assert.False(NoPaynSignature.Verify("wrong-key", Amount, Currency, OrderId, sig));
    }

    [Fact]
    public void Verify_RejectsMalformedSignature()
    {
        Assert.False(NoPaynSignature.Verify(Secret, Amount, Currency, OrderId, "not-a-hex-string"));
        Assert.False(NoPaynSignature.Verify(Secret, Amount, Currency, OrderId, ""));
        Assert.False(NoPaynSignature.Verify(Secret, Amount, Currency, OrderId, "zzzz"));
    }

    [Fact]
    public void Generate_DifferentInputsProduceDifferentSignatures()
    {
        var sig1 = NoPaynSignature.Generate(Secret, 100, "EUR", "order-1");
        var sig2 = NoPaynSignature.Generate(Secret, 200, "EUR", "order-1");
        var sig3 = NoPaynSignature.Generate(Secret, 100, "GBP", "order-1");
        var sig4 = NoPaynSignature.Generate(Secret, 100, "EUR", "order-2");

        Assert.NotEqual(sig1, sig2);
        Assert.NotEqual(sig1, sig3);
        Assert.NotEqual(sig1, sig4);
    }
}
