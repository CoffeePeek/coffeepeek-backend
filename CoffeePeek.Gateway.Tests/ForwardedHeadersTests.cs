using System.Net;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Shared.Web.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Gateway.Tests;

public class ForwardedHeadersTests
{
    [Theory]
    [InlineData("172.30.42.2", "203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:172.30.42.2", "203.0.113.7", "203.0.113.7")]
    [InlineData("172.30.42.2", "2001:db8::7", "2001:db8::7")]
    [InlineData("172.30.42.2", "1.2.3.4, 203.0.113.7", "203.0.113.7")]
    [InlineData("172.30.42.2", "1.2.3.4, 172.30.42.2", "172.30.42.2")]
    [InlineData("172.30.42.99", "1.2.3.4", "172.30.42.99")]
    [InlineData("127.0.0.1", "1.2.3.4", "127.0.0.1")]
    [InlineData("172.30.42.2", "invalid", "172.30.42.2")]
    [InlineData("172.30.42.2", "", "172.30.42.2")]
    public async Task Forwarding_OnlyConsumesOneHeaderFromTheKnownProxy(
        string peer, string forwarded, string expected)
    {
        await Check(peer, forwarded, "172.30.42.2", expected);
    }

    [Fact]
    public async Task NoConfiguredProxies_DoesNotTrustEvenLoopback() =>
        await Check("127.0.0.1", "1.2.3.4", null, "127.0.0.1");

    [Fact]
    public void InvalidProxyConfiguration_FailsInsteadOfTrustingEveryone()
    {
        using var services = Services("not-an-ip");
        var app = new ApplicationBuilder(services);
        app.UseForwardedHeaders();
        var build = () => app.Build();
        build.Should().Throw<FormatException>();
    }

    private static async Task Check(string peer, string forwarded, string? proxy, string expected)
    {
        using var services = Services(proxy);
        var app = new ApplicationBuilder(services);
        app.UseForwardedHeaders();
        app.Run(context =>
        {
            context.Connection.RemoteIpAddress!.ToString().Should().Be(expected);
            RateLimitingExtensions.GetClientKey(context).Should().Be(expected);
            context.Request.Headers.UserAgent.ToString().Should().Be("CoffeePeek/1.0 (Android)");
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = forwarded;
        context.Request.Headers.UserAgent = "CoffeePeek/1.0 (Android)";
        await app.Build()(context);
    }

    private static ServiceProvider Services(string? proxy)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            proxy is null ? [] : new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownProxies:0"] = proxy
            }).Build();
        return new ServiceCollection().AddLogging().AddTrustedProxyForwarding(configuration).BuildServiceProvider();
    }
}
