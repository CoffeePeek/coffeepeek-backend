using System.Globalization;
using System.Security.Claims;

namespace CoffeePeek.Shared.Auth;

public static class AccessTokenExpiry
{
    public const string ClaimType = "exp";

    public static DateTimeOffset? Read(ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(ClaimType)?.Value;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds < 0 || seconds > 253402300799) return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
