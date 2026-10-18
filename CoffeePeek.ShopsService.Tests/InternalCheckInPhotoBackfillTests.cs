using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.ShopsService.Tests;

public sealed class InternalCheckInPhotoBackfillTests
{
    private const string Path = "/internal/check-in-photo-backfill/candidates";

    [Fact]
    public async Task HttpEndpointRequiresInternalSecretBeforeReadingAndRejectsUnboundedOrInvalidPages()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var queries = new Queries();
        builder.Services.AddSingleton<ICheckInPhotoBackfillCandidates>(queries);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<GatewayAuthOptions>(o => o.SecretKey = "inventory-http-test-secret");
        builder.Services.AddControllers().AddApplicationPart(typeof(InternalCheckInPhotoBackfillController).Assembly);
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var before = DateTime.UtcNow.AddMinutes(-1);
            var request = new PhotoBackfillPageRequest(PhotoBackfillPhase.Home, before, before.AddDays(-7), null);
            using (var denied = await client.PostAsJsonAsync(Path, request, ct))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
                Assert.Contains("no-store", denied.Headers.CacheControl!.ToString()); Assert.Equal(0, queries.Calls);
            }
            client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "wrong-secret");
            using (var denied = await client.PostAsJsonAsync(Path, request, ct)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            client.DefaultRequestHeaders.Remove(GatewayHeaderConsts.XGatewayAuth);
            client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "inventory-http-test-secret");
            foreach (var invalid in new[]
            {
                request with { Limit = 101 }, request with { Limit = -1 }, request with { Phase = (PhotoBackfillPhase)999 },
                request with { BeforeUtc = DateTime.UtcNow.AddDays(1) }, request with { RecentSinceUtc = before.AddDays(1) },
                request with { Cursor = new PhotoBackfillCursor(before, Guid.NewGuid(), Guid.Empty) }
            })
            {
                using var bad = await client.PostAsJsonAsync(Path, invalid, ct);
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); Assert.Equal(0, queries.Calls);
            }
            using var ok = await client.PostAsJsonAsync(Path, request, ct);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode); Assert.Contains("no-store", ok.Headers.CacheControl!.ToString());
            var result = await ok.Content.ReadFromJsonAsync<PhotoBackfillPage>(ct);
            Assert.Empty(result!.Items); Assert.Equal(request, queries.Last); Assert.Equal(1, queries.Calls);
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public void CandidateEndpointIsNotMatchedByAnyConfiguredGatewayRoute()
    {
        var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "CoffeePeek.Gateway", "appsettings.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var route in document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").EnumerateObject())
        {
            var template = TemplateParser.Parse(route.Value.GetProperty("Match").GetProperty("Path").GetString()!.TrimStart('/'));
            Assert.False(new TemplateMatcher(template, new RouteValueDictionary())
                .TryMatch(new PathString(Path), new RouteValueDictionary()), route.Name);
        }
    }

    private sealed class Queries : ICheckInPhotoBackfillCandidates
    {
        public int Calls { get; private set; }
        public PhotoBackfillPageRequest? Last { get; private set; }
        public Task<PhotoBackfillPage> ReadAsync(PhotoBackfillPageRequest request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; Last = request; return Task.FromResult(new PhotoBackfillPage([], null)); }
    }
}
