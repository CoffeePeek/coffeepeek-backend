using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Infrastructure.Tests.Persistence;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInPhotoCanaryLiveTests(ShopsLiveDatabase fixture) : IClassFixture<ShopsLiveDatabase>
{
    [Fact]
    public async Task ExcludedAndAnonymousViewersPerformNoManifestSqlAndCanaryReadsOneBatchPerSurface()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken; var owner = Guid.NewGuid(); var canary = Guid.NewGuid();
        await using var writer = fixture.Context(false);
        var rows = await PhotoCanaryTestData.SeedAsync(writer, owner, ct);
        foreach (var viewer in new Guid?[] { owner, null, canary })
        {
            using var probe = new ReadPerformanceCommandProbe(capturePlans: true);
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options);
            var options = Options.Create(CheckInPhotoCanaryTests.Canary(canary));
            var queries = new CheckInQueries(db, PhotoCanaryTestData.Visibility(), options);
            var expected = viewer == canary;
            var detail = (await queries.GetUnifiedByIdAsync(rows.Shop.Id, viewer, ct))!;
            PhotoCanaryTestData.AssertPhotos(detail.Id, detail.Photos, detail.DrinkPhoto, expected);
            var home = (await queries.GetHomeByIdAsync(rows.Home.Id, viewer, ct))!;
            PhotoCanaryTestData.AssertPhotos(home.Id, home.Photos, home.DrinkPhoto, expected);
            var page = await queries.GetPublicAsync(viewer, null, null, owner, 10, null, ct);
            foreach (var item in page.Items) PhotoCanaryTestData.AssertPhotos(item.Id, item.Photos, item.DrinkPhoto, expected);
            var feed = await new CheckInFeedReader(db, PhotoCanaryTestData.Visibility(), options)
                .ReadAsync(new CheckInFeedScope(AuthorId: owner), 10, null, viewer, ct);
            foreach (var item in feed.Items) PhotoCanaryTestData.AssertPhotos(item.CheckIn.Id, item.CheckIn.Photos, item.CheckIn.DrinkPhoto, expected);
            var moderation = (await queries.GetModerationSnapshotAsync(rows.Shop.Id, rows.Shop.ContentRevision, ct))!;
            PhotoCanaryTestData.AssertPhotos(moderation.Id, moderation.Photos, moderation.DrinkPhoto, false);
            Assert.Equal(expected ? 4 : 0, probe.Commands.Count(c => c.CommandText.Contains("CheckInPhotoVariantManifests")));
            Assert.Empty(db.ChangeTracker.Entries());
        }
    }
}
