using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shared.Web.Extensions;

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddTrustedProxyForwarding(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            options.ForwardLimit = 1;
            // Empty trust lists mean "trust everyone" to the middleware, so disable forwarding instead.
            options.ForwardedHeaders = proxies.Length == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor;
            foreach (var proxy in proxies)
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        return services;
    }
}
