using CoffeePeek.Shared.Auth;

namespace CoffeePeek.Account.Application.Features.Admin.Cache;

internal static class AdminCachePatternValidator
{
    private static readonly char[] GlobChars = ['*', '?', '[', ']', '\\'];

    /// <summary>
    /// The segment before the first ':' must be a literal namespace, so globs such as "*:*" or "*user:*"
    /// cannot reach keys outside it. Revocation markers are never clearable: deleting them re-enables revoked tokens.
    /// </summary>
    public static bool IsAllowed(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var separator = pattern.IndexOf(':');
        if (separator <= 0)
            return false;

        var prefix = pattern[..separator];
        return prefix.IndexOfAny(GlobChars) < 0 && !IsProtectedPrefix(prefix);
    }

    public static bool IsAllowedKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var separator = key.IndexOf(':');
        return separator > 0 && !IsProtectedPrefix(key[..separator]);
    }

    private static bool IsProtectedPrefix(string prefix) =>
        prefix.Equals(RevokedUserKeys.Prefix, StringComparison.OrdinalIgnoreCase);
}
