using CoffeePeek.Shared.Kernel.Geocoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shared.Web.Geocoding;

public static class GeocodingServiceCollectionExtensions
{
    public static IServiceCollection AddYandexGeocoding(this IServiceCollection services)
    {
        services.AddOptions<YandexApiOptions>()
            .BindConfiguration(nameof(YandexApiOptions))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IAddressGeocoder, YandexGeocodingService>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<YandexApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        return services;
    }
}
