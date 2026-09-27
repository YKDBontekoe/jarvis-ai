using System.Security.Cryptography;
using System.Text;

namespace Jarvis.Application.Security;

/// <summary>Compares shared secrets without throwing on length mismatch.</summary>
public static class SecretComparer
{
    public static bool FixedTimeEquals(string? expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        if (expectedBytes.Length != suppliedBytes.Length)
        {
            CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
