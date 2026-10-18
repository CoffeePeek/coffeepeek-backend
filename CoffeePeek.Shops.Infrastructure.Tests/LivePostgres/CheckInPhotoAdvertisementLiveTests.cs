using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Infrastructure.Tests.Persistence;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInPhotoAdvertisementLiveTests(ShopsLiveDatabase fixture) : IClassFixture<ShopsLiveDatabase>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GlobalFlagControlsManifestSqlForEveryViewerAndReadsOneBatchPerSurface(bool advertise)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken; var owner = Guid.NewGuid();
        await using var writer = fixture.Context(false);
        var rows = await PhotoAdvertisementTestData.SeedAsync(writer, owner, ct);
        foreach (var viewer in new Guid?[] { owner, null, Guid.NewGuid() })
        {
            using var probe = new ReadPerformanceCommandProbe(capturePlans: true);
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options);
            var options = Options.Create(CheckInPhotoAdvertisementTests.Delivery(advertise));
            var queries = new CheckInQueries(db, PhotoAdvertisementTestData.Visibility(), options);
            var detail = (await queries.GetUnifiedByIdAsync(rows.Shop.Id, viewer, ct))!;
            PhotoAdvertisementTestData.AssertPhotos(detail.Id, detail.Photos, detail.DrinkPhoto, advertise);
            var home = (await queries.GetHomeByIdAsync(rows.Home.Id, viewer, ct))!;
            PhotoAdvertisementTestData.AssertPhotos(home.Id, home.Photos, home.DrinkPhoto, advertise);
            var page = await queries.GetPublicAsync(viewer, null, null, owner, 10, null, ct);
            foreach (var item in page.Items) PhotoAdvertisementTestData.AssertPhotos(item.Id, item.Photos, item.DrinkPhoto, advertise);
            var feed = await new CheckInFeedReader(db, PhotoAdvertisementTestData.Visibility(), options)
                .ReadAsync(new CheckInFeedScope(AuthorId: owner), 10, null, viewer, ct);
            foreach (var item in feed.Items) PhotoAdvertisementTestData.AssertPhotos(item.CheckIn.Id, item.CheckIn.Photos, item.CheckIn.DrinkPhoto, advertise);
            var moderation = (await queries.GetModerationSnapshotAsync(rows.Shop.Id, rows.Shop.ContentRevision, ct))!;
            PhotoAdvertisementTestData.AssertPhotos(moderation.Id, moderation.Photos, moderation.DrinkPhoto, false);
            Assert.Equal(advertise ? 4 : 0, probe.Commands.Count(c => c.CommandText.Contains("CheckInPhotoVariantManifests")));
            Assert.Empty(db.ChangeTracker.Entries());
        }
    }
}
