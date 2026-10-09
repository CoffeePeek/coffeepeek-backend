using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.Extensions.DependencyInjection;
using CoffeePeek.Shops.Persistance.Configuration;
using Wolverine.EntityFrameworkCore;

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
        services.AddScoped<MenuOcrJobStore>(provider =>
        {
            var db = provider.GetRequiredService<ShopsDbContext>();
            var outbox = provider.GetRequiredService<IDbContextOutbox>();
            outbox.Enroll(db);
            var apply = ActivatorUtilities.CreateInstance<ApplyShopMenuService>(provider, outbox);
            return new MenuOcrJobStore(db, apply, outbox);
        });
        services.AddScoped<IApplyApprovedShopMenuChangeService, ApplyApprovedShopMenuChangeService>();
        return services;
    }
}
