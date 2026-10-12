using System.Net;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Web.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Account.Infrastructure.Tests;

public class ClientIpProxyTests
{
    [Theory]
    [InlineData("172.30.42.2", "203.0.113.7", "203.0.113.7")]
    [InlineData("172.30.42.2", "1.2.3.4, 203.0.113.7", "203.0.113.7")]
    [InlineData("198.51.100.9", "1.2.3.4", "198.51.100.9")]
    public async Task Yarp_ReplacesUntrustedHeadersWithVerifiedIp(
        string gatewayPeer, string forwarded, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var accountBuilder = Host("127.0.0.1");
        await using var account = accountBuilder.Build();
        account.UseForwardedHeaders();
        account.MapGet("/metadata", (HttpContext context) =>
            $"{context.Connection.RemoteIpAddress}|{context.Request.Headers.UserAgent}|{context.Request.Headers["X-Forwarded-For"]}");
        await account.StartAsync(ct);

        var gatewayBuilder = Host("172.30.42.2");
        gatewayBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:metadata:ClusterId"] = "account",
            ["ReverseProxy:Routes:metadata:Match:Path"] = "/metadata",
            ["ReverseProxy:Clusters:account:Destinations:account:Address"] = account.Urls.Single()
        });
        gatewayBuilder.Services.Configure<GatewayAuthOptions>(o => o.SecretKey = "test-gateway-secret");
        gatewayBuilder.Services.AddServiceDiscovery();
        gatewayBuilder.Services.AddGatewayProxy(gatewayBuilder.Configuration);
        await using var gateway = gatewayBuilder.Build();
        // Model the Caddy (or direct caller) socket; the outgoing Gateway → Account hop uses real HTTP.
        gateway.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(gatewayPeer);
            return next(context);
        });
        gateway.UseForwardedHeaders();
        gateway.MapReverseProxy();
        await gateway.StartAsync(ct);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, gateway.Urls.Single() + "/metadata");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwarded);
        request.Headers.TryAddWithoutValidation("User-Agent", "CoffeePeek/1.0 (Android)");
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync(ct)).Should().Be($"{expected}|CoffeePeek/1.0 (Android)|");
    }

    private static WebApplicationBuilder Host(string proxy)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = proxy
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddTrustedProxyForwarding(builder.Configuration);
        return builder;
    }
}
