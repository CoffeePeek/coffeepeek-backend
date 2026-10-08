using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using StackExchange.Redis;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

/// <summary>Deterministic Redis fault at the durable consumer boundary.</summary>
public sealed class FailOnceStrictCacheInvalidator : IStrictCacheInvalidator
{
    private readonly Guid shopId;
    private readonly Func<CancellationToken, Task<bool>>? committedStateIsVisible;
    private readonly TaskCompletionSource<bool> firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int detailAttempts;

    public FailOnceStrictCacheInvalidator(
        Guid shopId,
        Func<CancellationToken, Task<bool>>? committedStateIsVisible = null)
    {
        this.shopId = shopId;
        this.committedStateIsVisible = committedStateIsVisible;
    }

    public Task<bool> FirstAttempt => firstAttempt.Task;
    public Task Recovered => recovered.Task;
    public int DetailAttempts => Volatile.Read(ref detailAttempts);

    public async Task RemoveAsync(CacheKey key, CancellationToken ct = default)
    {
        if (key.Key == CacheKey.Shop.Detail(shopId).Key)
        {
            if (Interlocked.Increment(ref detailAttempts) == 1)
            {
                firstAttempt.TrySetResult(committedStateIsVisible is null ||
                    await committedStateIsVisible(ct));
                throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Injected Redis outage");
            }
        }
        else if (key.Key == CacheKey.Shop.PublicPlatformStats().Key)
        {
            recovered.TrySetResult();
        }

    }

    public Task<int> RemoveByPatternAsync(string pattern, CancellationToken ct = default) => Task.FromResult(0);
}
