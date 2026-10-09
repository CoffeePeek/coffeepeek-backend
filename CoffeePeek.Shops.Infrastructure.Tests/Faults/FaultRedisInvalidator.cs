using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using StackExchange.Redis;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class FaultRedisInvalidator(RedisService redis, string root, string checkpoint) : IStrictCacheInvalidator
{
    public async Task RemoveAsync(CacheKey key, CancellationToken ct)
    {
        try
        {
            await ((IStrictCacheInvalidator)redis).RemoveAsync(key, ct);
            if (checkpoint == "after-delete")
            {
                File.WriteAllText(ShopsFaultControl.Marker(root, "redis-deleted"), "deleted");
                await Task.Delay(Timeout.Infinite, ct);
            }
        }
        catch (RedisException)
        {
            File.WriteAllText(ShopsFaultControl.Marker(root, "redis-failed"), "unavailable");
            throw;
        }
    }

    public Task<int> RemoveByPatternAsync(string pattern, CancellationToken ct) =>
        ((IStrictCacheInvalidator)redis).RemoveByPatternAsync(pattern, ct);
}
