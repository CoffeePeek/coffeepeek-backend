using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Shelf;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Persistance.Modules;

/// <summary>Registers the private coffee shelf persistence boundary within the Shops process.</summary>
public static class ShelfModule
{
    public static IServiceCollection AddShelfModule(this IServiceCollection services)
    {
        services.AddScoped<IShelfBagRepository, ShelfBagRepository>();
        services.AddScoped<IShelfCatalogLookup, ShelfCatalogLookup>();
        services.AddScoped<IShelfQueries, ShelfQueries>();
        return services;
    }
}
