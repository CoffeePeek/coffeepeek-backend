using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Application.Modules;

public static class MenuApplicationModule
{
    public static IServiceCollection AddMenuApplicationModule(this IServiceCollection services)
    {
        services.AddOptions<MenuPriceRangeOptions>()
            .BindConfiguration(nameof(MenuPriceRangeOptions));
        services.AddScoped<IApplyShopMenuService, ApplyShopMenuService>();
        return services;
    }
}
