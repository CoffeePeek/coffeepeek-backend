using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;

namespace CoffeePeek.Shared.Persistence.Cache.Redis;

/// <summary>
/// Cache invalidation for durable consumers. Failures propagate so the message can be retried.
/// </summary>
public interface IStrictCacheInvalidator
{
    Task RemoveAsync(CacheKey key, CancellationToken ct = default);
    Task<int> RemoveByPatternAsync(string pattern, CancellationToken ct = default);
}
