using System.Net;
using CoffeePeek.Gateway.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Gateway.Tests;

public class RateLimitingPartitionTests
{
    [Fact]
    public async Task SupportSubmissionPolicy_LimitsEachClientToFivePerHour()
    {
        var pipeline = BuildPipeline();
        for (var i = 0; i < 5; i++)
            (await Send(pipeline, "10.0.0.3", "POST", RateLimitingExtensions.SupportSubmissionPolicy)).Should().Be(200);

        (await Send(pipeline, "10.0.0.3", "POST", RateLimitingExtensions.SupportSubmissionPolicy)).Should().Be(429);
        (await Send(pipeline, "10.0.0.4", "POST", RateLimitingExtensions.SupportSubmissionPolicy)).Should().Be(200);
    }

    [Fact]
    public async Task AuthPolicy_ExhaustedByOneClient_DoesNotBlockAnotherClient()
    {
        var pipeline = BuildPipeline();

        for (var i = 0; i < 20; i++)
            (await Send(pipeline, "10.0.0.1", "POST", RateLimitingExtensions.AuthEndpointsPolicy)).Should().Be(200);

        (await Send(pipeline, "10.0.0.1", "POST", RateLimitingExtensions.AuthEndpointsPolicy)).Should().Be(429);
        (await Send(pipeline, "10.0.0.2", "POST", RateLimitingExtensions.AuthEndpointsPolicy)).Should().Be(200);
    }

    [Fact]
    public async Task ModerationSubmissionPolicy_DoesNotThrottleReads()
    {
        var pipeline = BuildPipeline();

        for (var i = 0; i < 15; i++)
            await Send(pipeline, "10.0.0.1", "POST", RateLimitingExtensions.ModerationSubmissionPolicy);

        (await Send(pipeline, "10.0.0.1", "POST", RateLimitingExtensions.ModerationSubmissionPolicy)).Should().Be(429);
        (await Send(pipeline, "10.0.0.1", "GET", RateLimitingExtensions.ModerationSubmissionPolicy)).Should().Be(200);
    }

    [Theory]
    [InlineData("1.2.3.4, 203.0.113.7")]
    [InlineData("198.51.100.9")]
    [InlineData("invalid")]
    public void ClientKey_IgnoresUnprocessedForwardedFor(string forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        RateLimitingExtensions.GetClientKey(context).Should().Be("198.51.100.9");
    }

    [Fact]
    public void ClientKey_UnknownPeerDoesNotFallBackToUntrustedHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.9";

        RateLimitingExtensions.GetClientKey(context).Should().Be("unknown");
    }

    [Fact]
    public async Task AuthPolicy_ChangingSpoofedHeaderDoesNotResetClientBudget()
    {
        var pipeline = BuildPipeline();
        for (var i = 0; i < 20; i++)
            (await Send(pipeline, "10.0.0.1", "POST", RateLimitingExtensions.AuthEndpointsPolicy, $"198.51.100.{i}"))
                .Should().Be(200);

        (await Send(pipeline, "10.0.0.1", "POST", RateLimitingExtensions.AuthEndpointsPolicy, "203.0.113.7"))
            .Should().Be(429);
    }

    private static RequestDelegate BuildPipeline()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddGatewayRateLimiting();
        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UseRateLimiter();
        app.Run(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        return app.Build();
    }

    private static async Task<int> Send(RequestDelegate pipeline, string ip, string method, string policy, string? forwardedFor = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (forwardedFor is not null)
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy)), "test"));
        await pipeline(context);
        return context.Response.StatusCode;
    }
}
