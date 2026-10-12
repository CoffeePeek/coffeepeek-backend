using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Application.Modules;
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

        services.AddSingleton<TypeAdapterConfig>(sp =>
            MapsterConfiguration.CreateConfig(sp.GetRequiredService<IOptions<MediaPublicUrlOptions>>().Value));

        services.AddSingleton<IMapper>(sp => new MapsterMapper.Mapper(sp.GetRequiredService<TypeAdapterConfig>()));

        services.AddShopsApplicationModule();
        services.AddMenuApplicationModule();
        services.AddCommunityApplicationModule();
        services.AddDiscoveryApplicationModule();

        return services;
    }
}
