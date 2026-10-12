using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Persistance.Modules;

public static class ShopsModule
{
    public static IServiceCollection AddShopsModule(this IServiceCollection services)
    {
        services.AddScoped<ICoffeeShopQueries, CoffeeShopQueries>();
        services.AddScoped<ICoffeeZoneQueries, CoffeeZoneQueries>();
        services.AddScoped<IQueryCoffeeShopRepository, QueryCoffeeShopRepository>();
        services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
        services.AddScoped<ICoffeeZoneRepository, CoffeeZoneRepository>();
        services.AddScoped<IAdminCoffeeShopQueryRepository, AdminCoffeeShopQueryRepository>();
        services.AddScoped<IQueryCityRepository, QueryCityRepository>();
        services.AddScoped<IQueryEquipmentRepository, QueryEquipmentRepository>();
        services.AddScoped<IQueryCoffeeBeanRepository, QueryCoffeeBeanRepository>();
        services.AddScoped<IQueryBrewMethodRepository, QueryBrewMethodRepository>();
        services.AddScoped<ICityRepository, CityRepository>();
        services.AddScoped<IEquipmentRepository, EquipmentRepository>();
        services.AddScoped<ICoffeeBeanRepository, CoffeeBeanRepository>();
        services.AddScoped<IBrewMethodRepository, BrewMethodRepository>();
        services.AddScoped<IAdminStatsQueryRepository, AdminStatsQueryRepository>();
        services.AddScoped<IPublicStatsQueryRepository, PublicStatsQueryRepository>();
        services.AddScoped<IShopTagRepository, ShopTagRepository>();
        services.AddScoped<IQueryShopTagRepository, QueryShopTagRepository>();
        return services;
    }
}
