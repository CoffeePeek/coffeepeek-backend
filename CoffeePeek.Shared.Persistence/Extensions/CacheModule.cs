using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shared.Persistence.Extensions;

public static class CacheModule
{
    public static IServiceCollection AddCacheModule(this IServiceCollection services)
    {
        services.RedisConfigurationOptions();
        services.AddSingleton<ICacheService, RedisService>();
        services.AddSingleton<ICacheInvalidationStrategy, CacheInvalidationStrategy>();
        services.AddSingleton<IRevokedUserStore, RedisRevokedUserStore>();
        
        return services;
    }
}

