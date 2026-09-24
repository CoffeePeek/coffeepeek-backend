using CoffeePeek.Account.Application.Features.Admin.Cache;
using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Admin.Cache;

public record ClearAdminCacheByPatternCommand([Required, MinLength(3)] string Pattern);

public record ClearAdminCacheResponse(int ClearedCount, string Pattern);

public static class ClearAdminCacheByPatternHandler
{
    public static async Task<Response<ClearAdminCacheResponse>> Handle(
        ClearAdminCacheByPatternCommand command,
        ICacheService cacheService,
        CancellationToken ct)
    {
        if (!AdminCachePatternValidator.IsAllowed(command.Pattern))
            return Response<ClearAdminCacheResponse>.Error("Pattern is not allowed. It must start with a literal 'prefix:' and cannot target revocation keys.");

        var clearedCount = await cacheService.RemoveByPattern(command.Pattern, ct);

        return Response<ClearAdminCacheResponse>.Success(new ClearAdminCacheResponse(clearedCount, command.Pattern));
    }
}

public record ClearAdminCacheKeyCommand([Required, MinLength(3)] string Key);

public static class ClearAdminCacheKeyHandler
{
    public static async Task<Response> Handle(
        ClearAdminCacheKeyCommand command,
        ICacheService cacheService,
        CancellationToken ct)
    {
        if (!AdminCachePatternValidator.IsAllowedKey(command.Key))
            return Response.Error("Key is not allowed.");

        await cacheService.RemoveAsync(new CacheKey(command.Key));
        return Response.Success(message: "Cache key removed.");
    }
}

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
