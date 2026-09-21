namespace CoffeePeek.Shared.Auth;

public interface IRevokedUserStore
{
    Task<bool> RevokeAsync(Guid userId, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<bool> IsRevokedAsync(Guid userId, CancellationToken cancellationToken = default);
}

public static class RevokedUserKeys
{
    public static string For(Guid userId) => $"revoked-user:{userId:D}";
}
