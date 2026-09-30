using CoffeePeek.Shops.Application.Extensions;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shared.Kernel.Options;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<MediaPublicUrlOptions>()
            .BindConfiguration(nameof(MediaPublicUrlOptions))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OpenAiOptions>()
            .BindConfiguration(nameof(OpenAiOptions))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MenuPriceRangeOptions>()
            .BindConfiguration(nameof(MenuPriceRangeOptions));

        services.AddOptions<MapClusteringOptions>()
            .BindConfiguration(MapClusteringOptions.SectionName)
            .Validate(o => o.ClusterMaxZoom >= 0 && o.ZoneMaxZoom > o.ClusterMaxZoom && o.ZoneMaxZoom <= 22)
            .Validate(o => o.ClusterCellPixels is >= 20 and <= 256)
            .Validate(o => o.MaxResponseItems is >= 1 and <= 5000)
            .ValidateOnStart();

        services.AddSingleton<TypeAdapterConfig>(sp =>
            MapsterConfiguration.CreateConfig(sp.GetRequiredService<IOptions<MediaPublicUrlOptions>>().Value));

        services.AddSingleton<IMapper>(sp => new MapsterMapper.Mapper(sp.GetRequiredService<TypeAdapterConfig>()));

        // Validation
        services.AddValidators();

        // Application Services
        services.AddScoped<PublicAddresses.PublicDiscoveryService>();
        services.AddScoped<ICreateShopFromModerationService, CreateShopFromModerationService>();
        services.AddScoped<ICreateShopFromImportService, CreateShopFromImportService>();
        services.AddScoped<IEnrichShopFromImportService, EnrichShopFromImportService>();
        services.AddScoped<IApplyShopMenuService, ApplyShopMenuService>();

        return services;
    }
}
