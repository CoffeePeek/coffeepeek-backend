using CoffeePeek.Shops.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Application.Modules;

public static class ShopsApplicationModule
{
    public static IServiceCollection AddShopsApplicationModule(this IServiceCollection services)
    {
        services.AddScoped<ICreateShopFromModerationService, CreateShopFromModerationService>();
        services.AddScoped<ICreateShopFromImportService, CreateShopFromImportService>();
        services.AddScoped<IEnrichShopFromImportService, EnrichShopFromImportService>();
        return services;
    }
}
