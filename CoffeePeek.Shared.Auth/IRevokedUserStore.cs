using CoffeePeek.Shared.Auth.Options;

namespace CoffeePeek.Shared.Auth;

public interface IRevokedUserStore
{
    /// <summary>Rejects every access token of the user issued before now. Tokens issued later stay valid.</summary>
    Task<bool> RevokeAsync(Guid userId, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<bool> IsRevokedAsync(Guid userId, DateTimeOffset? issuedAt, CancellationToken cancellationToken = default);
}

public static class RevokedUserKeys
{
    public const string Prefix = "revoked-user";

    public static string For(Guid userId) => $"{Prefix}:{userId:D}";

    /// <param name="marker">Stored value: unix seconds of the revocation, or null when the user has no marker.</param>
    /// <param name="issuedAt">Token <c>iat</c>; null for tokens issued before <c>iat</c> was added.</param>
    public static bool IsTokenRevoked(string? marker, DateTimeOffset? issuedAt)
    {
        if (marker is null)
            return false;

        // Legacy markers were the literal "1" (revoke everything); they expire within one access-token lifetime.
        if (!long.TryParse(marker, out var revokedAtUnix) || revokedAtUnix <= 1 || issuedAt is null)
            return true;

        // ponytail: iat has second precision, so a token issued in the same second as the revocation survives.
        return issuedAt.Value.ToUnixTimeSeconds() < revokedAtUnix;
    }
}

public static class RevokedUserStoreExtensions
{
    /// <summary>Revokes the user's current access tokens for one access-token lifetime; throws if Redis rejects the write.</summary>
    public static async Task RevokeAccessTokensAsync(
        this IRevokedUserStore store,
        Guid userId,
        JWTOptions jwtOptions,
        CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(jwtOptions.AccessTokenLifetimeMinutes);
        if (!await store.RevokeAsync(userId, ttl, ct))
            throw new InvalidOperationException("Could not persist access-token revocation.");
    }
}
