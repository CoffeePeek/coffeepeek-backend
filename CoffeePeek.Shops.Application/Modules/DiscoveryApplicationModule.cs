using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Application.PublicAddresses;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Application.Modules;

public static class DiscoveryApplicationModule
{
    public static IServiceCollection AddDiscoveryApplicationModule(this IServiceCollection services)
    {
        services.AddOptions<MapClusteringOptions>()
            .BindConfiguration(MapClusteringOptions.SectionName)
            .Validate(o => o.ClusterMaxZoom >= 0 && o.ZoneMaxZoom > o.ClusterMaxZoom && o.ZoneMaxZoom <= 22)
            .Validate(o => o.ClusterCellPixels is >= 20 and <= 256)
            .Validate(o => o.MaxResponseItems is >= 1 and <= 5000)
            .ValidateOnStart();
        services.AddScoped<PublicDiscoveryService>();
        return services;
    }
}
