using CoffeePeek.Gateway.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Forwarder;

namespace CoffeePeek.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring the YARP reverse proxy pipeline.
/// Routes and clusters are loaded from the "ReverseProxy" section of appsettings.json —
/// no code change is required to add or modify routes.
/// </summary>
public static class YarpExtensions
{
    /// <summary>
    /// Registers YARP with configuration-based routing, service discovery,
    /// and the gateway's custom transform providers.
    /// </summary>
    public static IServiceCollection AddGatewayProxy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var proxyBuilder = services
            .AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"))
            .AddServiceDiscoveryDestinationResolver();

        // YARP owns its transport and does not use the default HttpClientFactory
        // resilience pipeline. Wrap it explicitly so a short downstream restart
        // does not surface as a 502 for safe, idempotent requests.
        services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory,
            ResilientForwarderHttpClientFactory>());

        // Claims-to-headers: strips incoming X-User-* headers (anti-spoofing)
        // and re-adds them from the validated JWT claims.
        proxyBuilder.AddTransforms<ClaimsToHeadersTransformProvider>();

        // Correlation ID: propagates X-Correlation-Id to downstream services
        // and echoes it back to the client in the response.
        proxyBuilder.AddTransforms<CorrelationIdTransformProvider>();

        return services;
    }
}
