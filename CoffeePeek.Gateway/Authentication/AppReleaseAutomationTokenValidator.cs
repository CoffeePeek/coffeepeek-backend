using System.Security.Cryptography;
using System.Text;

namespace CoffeePeek.Gateway.Authentication;

public static class AppReleaseAutomationTokenValidator
{
    public static bool IsValid(string? configuredToken, string? providedToken)
    {
        if (string.IsNullOrWhiteSpace(configuredToken)
            || string.IsNullOrWhiteSpace(providedToken)
            || configuredToken.Length < 32)
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredToken));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedToken));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
