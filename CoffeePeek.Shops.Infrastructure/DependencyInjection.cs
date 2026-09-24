using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Infrastructure.Account;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Infrastructure.Media;
using CoffeePeek.Shops.Infrastructure.Menu;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient("account-user-lookup", client =>
        {
            client.BaseAddress = new Uri($"http://{AppResources.AccountService}");
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddHttpClient(MediaUploadedPhotoLookup.HttpClientName, client =>
        {
            client.BaseAddress = new Uri($"http://{AppResources.MediaService}");
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        var timeoutRaw = configuration.GetSection(nameof(OpenAiOptions))["TimeoutSeconds"];
        var openAiTimeoutSeconds = int.TryParse(timeoutRaw, out var parsedTimeout)
            ? parsedTimeout
            : 90;

        services.AddHttpClient("openai", (sp, client) =>
        {
            var timeout = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value.TimeoutSeconds;
            // Resilience pipeline owns the deadline; keep HttpClient.Timeout at or above it.
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeout, 30, 180) + 10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CoffeePeek.ShopsService/1.0");
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            var handler = new SocketsHttpHandler();
            var proxy = OpenAiProxy.Create(settings.ProxyUrl);
            if (proxy is not null)
            {
                handler.UseProxy = true;
                handler.Proxy = proxy;
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("CoffeePeek.Shops.Infrastructure.Menu.OpenAiProxy")
                    .LogInformation(
                        "OpenAI HTTP client using proxy {Proxy}",
                        OpenAiProxy.DisplayHost(settings.ProxyUrl));
            }

            return handler;
        })
        // Aspire AddStandardResilienceHandler() defaults to 10s/attempt and 30s total,
        // which aborts OpenAI vision (menu photos) long before OpenAiOptions.TimeoutSeconds.
        .RemoveAllResilienceHandlers()
        .AddStandardResilienceHandler(options => ApplyOpenAiResilience(options, openAiTimeoutSeconds));

        services.AddHttpClient("menu-photos", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddScoped<IUserExistenceLookup, AccountUserExistenceLookup>();
        services.AddScoped<IUploadedPhotoLookup, MediaUploadedPhotoLookup>();
        services.AddScoped<IMenuVisionParser, OpenAiMenuVisionParser>();
        services.AddScoped<IMenuPhotoDownloader, HttpMenuPhotoDownloader>();
        services.AddScoped<ModerationShopApproveHandler>();
        services.AddScoped<ICreateRoasterFromModerationService, CreateRoasterFromModerationService>();
        services.AddScoped<ModerationRoasterApproveHandler>();
        services.AddScoped<ShopChangeRequestApprovedHandler>();

        return services;
    }

    internal static void ApplyOpenAiResilience(HttpStandardResilienceOptions options, int timeoutSeconds)
    {
        var attempt = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 30, 180));
        options.AttemptTimeout.Timeout = attempt;
        options.TotalRequestTimeout.Timeout = attempt + TimeSpan.FromSeconds(5);
        // StandardResilienceHandler rejects MaxRetryAttempts = 0 (shops failed to boot).
        // Keep a single retry for fast 5xx; the long AttemptTimeout still covers vision.
        options.Retry.MaxRetryAttempts = 1;
        // Vision calls are POSTs and billed per attempt; a retry after a timeout is billed and then cut off by the
        // 5 s left in TotalRequestTimeout anyway. Never retry them in the HTTP pipeline.
        options.Retry.DisableForUnsafeHttpMethods();
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(attempt.Ticks * 2) + TimeSpan.FromSeconds(1);
        options.CircuitBreaker.MinimumThroughput = 20;
    }
}
