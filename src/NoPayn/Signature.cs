using System.Security.Cryptography;
using System.Text;

namespace NoPayn;

/// <summary>
/// HMAC-SHA256 signature utilities for NoPayn payment verification.
/// Canonical message format: <c>{amount}:{currency}:{orderId}</c>
/// </summary>
public static class NoPaynSignature
{
    /// <summary>
    /// Generate a hex-encoded HMAC-SHA256 signature.
    /// </summary>
    public static string Generate(string secret, int amount, string currency, string orderId)
    {
        var message = $"{amount}:{currency}:{orderId}";
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hash = HMACSHA256.HashData(keyBytes, messageBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time verification of an HMAC-SHA256 signature.
    /// </summary>
    public static bool Verify(string secret, int amount, string currency, string orderId, string signature)
    {
        var expected = Generate(secret, amount, currency, orderId);

        if (expected.Length != signature.Length)
            return false;

        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var signatureBytes = Convert.FromHexString(signature);
            return CryptographicOperations.FixedTimeEquals(expectedBytes, signatureBytes);
        }
        catch
        {
            return false;
        }
    }
}
