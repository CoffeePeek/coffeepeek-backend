using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Infrastructure.Services;
using CoffeePeek.Moderation.Infrastructure.Shops;
using CoffeePeek.Moderation.Infrastructure.Notifications;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Extentions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace CoffeePeek.Moderation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<TelegramModerationOptions>()
            .BindConfiguration("TelegramModeration")
            .Validate(options => string.IsNullOrWhiteSpace(options.BotToken) == string.IsNullOrWhiteSpace(options.ChatId),
                "Telegram moderation bot token and chat ID must be configured together.")
            .ValidateOnStart();
        services.AddHttpClient<TelegramModerationClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.telegram.org/");
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .RemoveAllLoggers()
            .RemoveAllResilienceHandlers();

        var yandexOptions = services.AddValidateOptions<YandexApiOptions>();
        services.AddHttpClient<IYandexGeocodingService, YandexGeocodingService>(client =>
        {
            client.BaseAddress = new Uri(yandexOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(yandexOptions.TimeoutSeconds);
        });

        var googlePlacesOptions = services.AddValidateOptions<GooglePlaces>();
        services.AddHttpClient<IGooglePlacesLookup, GooglePlacesLookup>(client =>
        {
            client.BaseAddress = new Uri(googlePlacesOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(googlePlacesOptions.TimeoutSeconds, 5, 60));
        });

        // Aspire AddStandardResilienceHandler() defaults to 30s total timeout and overrides HttpClient.Timeout.
        // Overpass QL for Minsk is declared as [timeout:90] — replace the default pipeline for this client only.
        var attemptTimeout = TimeSpan.FromSeconds(120);

        services.AddHttpClient<IOverpassClient, OverpassClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CoffeePeek/1.0 (https://coffeepeek.by; osm-import)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            // HttpClient's default 100 s timeout wraps the whole pipeline and would kill the 120 s attempt
            // before Polly's timeouts or retry ever run; let the resilience handler own timeouts.
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .RemoveAllResilienceHandlers()
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = attemptTimeout;
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(150);
            options.Retry.MaxRetryAttempts = 1;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(300);
            options.CircuitBreaker.MinimumThroughput = 100;
        });

        services.AddHttpClient("shops-roaster-lookup", client =>
        {
            client.BaseAddress = new Uri($"http://{AppResources.ShopsService}");
            // This client is on interactive submit paths; a slow Shops lookup must fail promptly.
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddScoped<IRoasterExistenceLookup, ShopsRoasterExistenceLookup>();
        services.AddScoped<CoffeePeek.Shared.Auth.PublicAddressHttpClient>();
        services.AddScoped<IPublicAddressLookup, ShopsPublicAddressLookup>();


        return services;
    }
}
