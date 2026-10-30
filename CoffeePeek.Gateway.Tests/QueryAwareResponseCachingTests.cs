using System.Net;
using CoffeePeek.Gateway.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Gateway.Tests;

public sealed class QueryAwareResponseCachingTests
{
    [Fact]
    public async Task GatewayCacheSeparatesQueryValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddResponseCaching();
        await using var app = builder.Build();
        app.UseQueryAwareResponseCaching();
        var calls = 0;
        app.MapGet("/api/coffees", (HttpContext context) =>
        {
            calls++;
            context.Response.Headers.CacheControl = "public, max-age=120";
            return Results.Text(context.Request.Query["search"].ToString());
        });
        await app.StartAsync(ct);

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var first = await client.GetAsync("/api/coffees?search=one", ct);
        using var repeated = await client.GetAsync("/api/coffees?search=one", ct);
        using var second = await client.GetAsync("/api/coffees?search=two", ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("one", await first.Content.ReadAsStringAsync(ct));
        Assert.Equal("one", await repeated.Content.ReadAsStringAsync(ct));
        Assert.Equal("two", await second.Content.ReadAsStringAsync(ct));
        Assert.Equal(2, calls);
    }
}
