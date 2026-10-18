using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoBackfillInventoryHttpTests
{
    private const string Path = "/internal/check-in-photo-backfill/inspect";

    [Fact]
    public async Task RealHttpInspectorRequiresTrustedSecretLimitsPageAndReturnsOnlySafeReadonlyNoStoreReport()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var database = Guid.NewGuid().ToString(); var objects = new ForbiddenObjects();
        builder.Services.AddDbContext<MediaDbContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddScoped<PhotoBackfillInspector>(); builder.Services.AddSingleton<IPhotoBackfillObjectReader>(objects);
        builder.Services.Configure<GatewayAuthOptions>(o => o.SecretKey = "inventory-http-test-secret");
        builder.Services.AddControllers().AddApplicationPart(typeof(InternalPhotoBackfillInventoryController).Assembly);
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync(ct);
        try
        {
            var actions = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
            Assert.Contains(actions.OfType<ControllerActionDescriptor>(),
                action => action.ControllerTypeInfo.AsType() == typeof(InternalPhotoBackfillInventoryController));
            var descriptions = app.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
                .ApiDescriptionGroups.Items.SelectMany(group => group.Items);
            Assert.DoesNotContain(descriptions, description => description.ActionDescriptor is ControllerActionDescriptor action
                && action.ControllerTypeInfo.AsType() == typeof(InternalPhotoBackfillInventoryController));
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var candidates = Enumerable.Range(0, 100).Select(_ => PhotoBackfillInventoryTests.Binding().Candidate).ToArray();
            using (var denied = await client.PostAsJsonAsync(Path, candidates, ct))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Contains("no-store", denied.Headers.CacheControl!.ToString());
            }
            client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "wrong-secret");
            using (var denied = await client.PostAsJsonAsync(Path, candidates, ct)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            client.DefaultRequestHeaders.Remove(GatewayHeaderConsts.XGatewayAuth);
            client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "inventory-http-test-secret");
            foreach (var invalid in new[] { Array.Empty<PhotoBackfillCandidate>(), candidates.Append(PhotoBackfillInventoryTests.Binding().Candidate).ToArray(), new[] { candidates[0], candidates[0] }, new PhotoBackfillCandidate[] { null! } })
            {
                using var bad = await client.PostAsJsonAsync(Path, invalid, ct); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
                Assert.Contains("no-store", bad.Headers.CacheControl!.ToString());
            }
            using var accepted = await client.PostAsJsonAsync(Path + "?verifyContent=false", candidates, ct);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode); Assert.Contains("no-store", accepted.Headers.CacheControl!.ToString());
            var text = await accepted.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<PhotoBackfillInspection[]>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(100, result.Length); Assert.All(result, x => { Assert.Equal("media_missing", x.EligibilityFailure); Assert.Equal("not_checked", x.SourceStatus); });
            Assert.DoesNotContain("storageKey", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("X-Amz", text, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("private-bucket", text);
            Assert.Equal(0, objects.Calls);
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            Assert.Empty(await db.Photos.ToArrayAsync(ct)); Assert.Empty(await db.PhotoVariantJobs.ToArrayAsync(ct));
            Assert.Empty(await db.PhotoVariants.ToArrayAsync(ct)); Assert.Empty(await db.PhotoVariantArtifacts.ToArrayAsync(ct));
            Assert.Empty(await db.CheckInPhotoTombstones.ToArrayAsync(ct)); Assert.Empty(db.ChangeTracker.Entries());
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public void InventoryEndpointHasNoMatchingPublicGatewayRoute()
    {
        var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CoffeePeek.Gateway", "appsettings.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var route in document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").EnumerateObject())
        {
            var template = TemplateParser.Parse(route.Value.GetProperty("Match").GetProperty("Path").GetString()!.TrimStart('/'));
            Assert.False(new TemplateMatcher(template, new RouteValueDictionary()).TryMatch(new PathString(Path), new RouteValueDictionary()), route.Name);
        }
    }

    private sealed class ForbiddenObjects : IPhotoBackfillObjectReader
    {
        public int Calls { get; private set; }
        public Task<PhotoBackfillObject> OpenAsync(string key, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Ineligible bindings must never inspect storage."); }
    }
}
