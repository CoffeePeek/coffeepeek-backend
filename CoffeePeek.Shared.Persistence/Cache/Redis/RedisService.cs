using System.Text.Json;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CoffeePeek.Shared.Persistence.Cache.Redis;

public class RedisService : ICacheService, IStrictCacheInvalidator
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;
    private readonly IServer? _server;

    public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;

        var endpoint = redis.GetEndPoints().FirstOrDefault();
        _server = endpoint is null ? null : redis.GetServer(endpoint);
    }

    public async Task<T?> GetAsync<T>(CacheKey cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? value = await _db.StringGetAsync(cacheKey.Key);
            return string.IsNullOrEmpty(value) ? default : JsonSerializer.Deserialize<T>(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis GetAsync unavailable for key {CacheKey}", cacheKey.Key);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis GetAsync failed for key {CacheKey}", cacheKey.Key);
            return default;
        }
    }

    public async Task<T?> GetAsync<T>(
        CacheKey cacheKey, 
        Func<CancellationToken, Task<T>> factory, 
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        T result;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? cachedValue = await _db.StringGetAsync(cacheKey.Key);

            if (!string.IsNullOrEmpty(cachedValue))
            {
                return JsonSerializer.Deserialize<T>(cachedValue);
            }
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis GetOrCreateAsync read unavailable for key {CacheKey}", cacheKey.Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis GetOrCreateAsync read failed for key {CacheKey}", cacheKey.Key);
        }

        result = await factory(cancellationToken);

        if (result != null)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _db.StringSetAsync(
                    cacheKey.Key, 
                    JsonSerializer.Serialize(result), 
                    expiration ?? TimeSpan.FromMinutes(10));
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogWarning(ex, "Redis GetOrCreateAsync write unavailable for key {CacheKey}", cacheKey.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis GetOrCreateAsync write failed for key {CacheKey}", cacheKey.Key);
            }
        }

        return result;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            await _db.StringSetAsync(key, JsonSerializer.Serialize(value), new Expiration(expiry ?? TimeSpan.FromHours(1)));
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis SetAsync unavailable for key {CacheKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SetAsync failed for key {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _db.KeyDeleteAsync(key);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis RemoveAsync unavailable for key {CacheKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis RemoveAsync failed for key {CacheKey}", key);
        }
    }

    public async Task SetAsync<T>(CacheKey cacheKey, T value, TimeSpan? customTtl = null)
    {
        try
        {
            var ttl = customTtl ?? cacheKey.DefaultTtl ?? TimeSpan.FromDays(1);
            await _db.StringSetAsync(cacheKey.Key, JsonSerializer.Serialize(value), new Expiration(ttl));
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis SetAsync unavailable for key {CacheKey}", cacheKey.Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SetAsync failed for key {CacheKey}", cacheKey.Key);
        }
    }

    public async Task RemoveAsync(CacheKey cacheKey)
    {
        try
        {
            await _db.KeyDeleteAsync(cacheKey.Key);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis RemoveAsync unavailable for key {CacheKey}", cacheKey.Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis RemoveAsync failed for key {CacheKey}", cacheKey.Key);
        }
    }

    public async Task<bool> ExistsAsync(CacheKey cacheKey)
    {
        try
        {
            return await _db.KeyExistsAsync(cacheKey.Key);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis ExistsAsync unavailable for key {CacheKey}", cacheKey.Key);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis ExistsAsync failed for key {CacheKey}", cacheKey.Key);
            return false;
        }
    }

    public async Task<int> RemoveByPattern(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_server is null)
            {
                _logger.LogWarning("Redis RemoveByPattern skipped because no Redis endpoints are configured");
                return 0;
            }

            var keysToDelete = await CollectKeysByPatternAsync(pattern, int.MaxValue, cancellationToken);
            if (keysToDelete.Count > 0)
                await _db.KeyDeleteAsync(keysToDelete.ToArray());

            return keysToDelete.Count;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis RemoveByPattern unavailable for pattern {Pattern}", pattern);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis RemoveByPattern failed for pattern {Pattern}", pattern);
            return 0;
        }
    }

    async Task IStrictCacheInvalidator.RemoveAsync(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _db.KeyDeleteAsync(key.Key);
    }

    async Task<int> IStrictCacheInvalidator.RemoveByPatternAsync(string pattern, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_server is null)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect,
                "Redis has no endpoint for strict cache invalidation.");

        var keys = await CollectKeysByPatternAsync(pattern, int.MaxValue, ct);
        if (keys.Count > 0)
            await _db.KeyDeleteAsync(keys.ToArray());

        return keys.Count;
    }

    public async Task<IReadOnlyList<string>> GetKeysByPatternAsync(
        string pattern,
        int limit,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_server is null)
            {
                _logger.LogWarning("Redis GetKeysByPatternAsync skipped because no Redis endpoints are configured");
                return [];
            }

            var keys = await CollectKeysByPatternAsync(pattern, limit, cancellationToken);
            return keys.Select(k => k.ToString()).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis GetKeysByPatternAsync unavailable for pattern {Pattern}", pattern);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis GetKeysByPatternAsync failed for pattern {Pattern}", pattern);
            return [];
        }
    }

    private async Task<List<RedisKey>> CollectKeysByPatternAsync(
        string pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var keys = new List<RedisKey>();
        await foreach (var key in _server!.KeysAsync(pattern: pattern, pageSize: 250).WithCancellation(cancellationToken))
        {
            keys.Add(key);
            if (keys.Count >= limit)
                break;
        }

        return keys;
    }
}
