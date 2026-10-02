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
        services.AddUserRevocationStore();
        services.AddSingleton<RedisService>();
        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<RedisService>());
        services.AddSingleton<IStrictCacheInvalidator>(sp => sp.GetRequiredService<RedisService>());
        services.AddSingleton<ICacheInvalidationStrategy, CacheInvalidationStrategy>();
        
        return services;
    }

    public static IServiceCollection AddUserRevocationStore(this IServiceCollection services)
    {
        services.RedisConfigurationOptions();
        services.AddSingleton<IRevokedUserStore, RedisRevokedUserStore>();

        return services;
    }
}
