using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Persistance.Modules;

public static class MenuModule
{
    public static IServiceCollection AddMenuModule(this IServiceCollection services)
    {
        services.AddScoped<IQueryCoffeeDrinkRepository, QueryCoffeeDrinkRepository>();
        services.AddScoped<ICoffeeDrinkRepository, QueryCoffeeDrinkRepository>();
        services.AddScoped<IDrinkCatalog, QueryCoffeeDrinkRepository>();
        services.AddScoped<IQueryShopMenuRepository, QueryShopMenuRepository>();
        services.AddScoped<IShopMenuRepository, ShopMenuRepository>();
        services.AddScoped<MenuOcrJobStore>();
        services.AddScoped<IApplyApprovedShopMenuChangeService, ApplyApprovedShopMenuChangeService>();
        return services;
    }
}
