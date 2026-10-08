using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Persistance.Modules;

public static class DiscoveryModule
{
    public static IServiceCollection AddDiscoveryModule(this IServiceCollection services)
    {
        services.AddScoped<IDiscoveryQueryStore, DiscoveryQueryStore>();
        services.AddScoped<IDiscoveryFavoritesStore, DiscoveryFavoritesStore>();
        return services;
    }
}
