using CoffeePeek.Shared.Auth;
using StackExchange.Redis;

namespace CoffeePeek.Shared.Persistence.Cache.Redis;

public sealed class RedisRevokedUserStore(IConnectionMultiplexer redis) : IRevokedUserStore
{
    private readonly IDatabase _database = redis.GetDatabase();

    public Task<bool> RevokeAsync(
        Guid userId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _database.StringSetAsync(
            RevokedUserKeys.For(userId),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            new Expiration(ttl),
            When.Always);
    }

    public async Task<bool> IsRevokedAsync(
        Guid userId,
        DateTimeOffset? issuedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var marker = await _database.StringGetAsync(RevokedUserKeys.For(userId));
        return RevokedUserKeys.IsTokenRevoked(marker.IsNull ? null : marker.ToString(), issuedAt);
    }
}
