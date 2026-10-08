using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Persistance.Modules;

public static class CoffeeCatalogModule
{
    public static IServiceCollection AddCoffeeCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<CoffeeCatalogStore>();
        services.AddScoped<ICoffeeCatalogStore>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
        services.AddScoped<IPublicCoffeeCatalogReader>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
        services.AddScoped<ICoffeeProductWriter>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
        services.AddScoped<ICoffeeImportWriter>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
        services.AddScoped<IDiscoveryClassificationStore, DiscoveryClassificationStore>();
        services.AddScoped<IQueryRoasterRepository, QueryRoasterRepository>();
        services.AddScoped<IRoasterRepository, RoasterRepository>();
        return services;
    }
}
