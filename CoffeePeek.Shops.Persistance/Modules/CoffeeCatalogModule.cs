using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Persistance.Modules;

public static class CoffeeCatalogModule
{
    public static IServiceCollection AddCoffeeCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<CoffeeCatalogStore>();
        services.AddScoped<IAdminCoffeeCatalogReader>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
        services.AddScoped<ICoffeeCatalogTagStorage, CoffeeCatalogTagStorage>();
        services.AddScoped<ICoffeeCatalogTagWriter, CoffeeCatalogTagWriter>();
        services.AddScoped<IPublicCoffeeCatalogReader>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
        services.AddScoped<ICoffeeProductStorage, CoffeeProductStorage>();
        services.AddScoped<ICoffeeProductWriter, CoffeeProductWriter>();
        services.AddScoped<ICoffeeImportStorage, CoffeeImportStorage>();
        services.AddScoped<ICoffeeImportWriter>(provider => new CoffeeImportService(
            provider.GetRequiredService<ICoffeeImportStorage>(), provider.GetRequiredService<IOptions<CoffeeImportOptions>>(),
            provider.GetRequiredService<ILogger<CoffeeImportService>>()));
        services.AddScoped<IDiscoveryClassificationStore, DiscoveryClassificationStore>();
        services.AddScoped<IQueryRoasterRepository, QueryRoasterRepository>();
        services.AddScoped<IRoasterRepository, RoasterRepository>();
        services.AddScoped<IRoasterOwnershipWriter, RoasterOwnershipWriter>();
        return services;
    }
}
