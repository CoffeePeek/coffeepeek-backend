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
            "1",
            new Expiration(ttl),
            When.Always);
    }

    public Task<bool> IsRevokedAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _database.KeyExistsAsync(RevokedUserKeys.For(userId));
    }
}
