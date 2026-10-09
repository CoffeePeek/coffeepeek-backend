using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Application.Modules;

public static class ShopsApplicationModule
{
    public static IServiceCollection AddShopsApplicationModule(this IServiceCollection services)
    {
        services.AddScoped<IShopPriceRangeWriter, ShopPriceRangeWriter>();
        services.AddScoped<ICreateShopFromModerationService, CreateShopFromModerationService>();
        services.AddScoped<ICreateShopFromImportService, CreateShopFromImportService>();
        services.AddScoped<IEnrichShopFromImportService, EnrichShopFromImportService>();
        return services;
    }
}
