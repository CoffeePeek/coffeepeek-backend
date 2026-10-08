using CoffeePeek.Moderation.Application.Features.Shop.CreateShop;
using CoffeePeek.Moderation.Application.Mapper;
using CoffeePeek.Shared.Kernel.Options;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<MediaPublicUrlOptions>()
            .BindConfiguration(nameof(MediaPublicUrlOptions))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IMapper>(sp =>
            MapsterConfiguration.CreateMapper(sp.GetRequiredService<IOptions<MediaPublicUrlOptions>>().Value));
        services.AddScoped<Services.PublicModerationService>();
        services.AddScoped<IModerationShopCreationService, ModerationShopCreationService>();
        return services;
    }
}
