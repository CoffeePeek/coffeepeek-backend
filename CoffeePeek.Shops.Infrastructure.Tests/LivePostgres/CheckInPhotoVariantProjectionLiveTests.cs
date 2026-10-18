using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Events.Photo;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Community;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInPhotoVariantProjectionLiveTests(ShopsLiveDatabase fixture) : IClassFixture<ShopsLiveDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly IOptions<CheckInPhotoDeliveryOptions> Enabled = Options.Create(new CheckInPhotoDeliveryOptions
        { AdvertiseVariants = true, ServeVariants = true });
    private static IAuthorVisibilityLookup Visibility()
    {
        var visibility = new Mock<IAuthorVisibilityLookup>();
        visibility.Setup(v => v.ExcludedUserIdsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return visibility.Object;
    }

    [Fact]
    public async Task AdditiveMigrationPreservesExistingPhotoIdsKeysAndContentRevision()
    {
        RequireLive();
        var connection = await LivePostgresServer.CreateDatabaseAsync("photovariantupgrade", Ct);
        try
        {
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
            await db.GetService<IMigrator>().MigrateAsync("20261015142731_AddDrinkFavorites", Ct);
            var row = await Seed(db, 2);
            var ids = row.ShopPhotos.Select(p => p.Id).ToArray();
            var keys = row.ShopPhotos.Select(p => p.StorageKey).Order().ToArray();
            var revision = row.ContentRevision;
            await db.Database.MigrateAsync(Ct); db.ChangeTracker.Clear();
            var upgraded = await db.CheckIns.Include(c => c.ShopPhotos).SingleAsync(c => c.Id == row.Id, Ct);
            Assert.Equal(ids.Order(), upgraded.ShopPhotos.Select(p => p.Id).Order());
            Assert.Equal(keys, upgraded.ShopPhotos.Select(p => p.StorageKey).Order());
            Assert.Equal(revision, upgraded.ContentRevision);
            Assert.Empty(await db.CheckInPhotoVariantManifests.ToArrayAsync(Ct));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    [Fact]
    public async Task ReadyAppearsInNextDetailAndHomeReadsWithoutRevisingOrRepublishingTheCheckIn()
    {
        RequireLive();
        await using var db = fixture.Context(false);
        var row = await Seed(db, 3);
        var revision = row.ContentRevision;
        var published = await db.CheckIns.AsNoTracking().Where(c => c.Id == row.Id).Select(c => c.FirstPublishedAtUtc).SingleAsync(Ct);
        var queries = new CheckInQueries(db, Visibility(), Enabled);
        var before = (await queries.GetUnifiedByIdAsync(row.Id, row.UserId, Ct))!;
        Assert.All(before.Photos, p => Assert.Null(p.Variants)); Assert.Null(before.DrinkPhoto!.Variants);
        var projection = new CheckInPhotoVariantProjection(db);
        foreach (var photo in row.ShopPhotos)
        {
            var ready = Ready(row, photo, 2);
            await projection.ApplyAsync(ready, Ct);
            await projection.ApplyAsync(ready, Ct); // duplicate
            await projection.ApplyAsync(Ready(row, photo, 1), Ct); // older delivery cannot replace v2
        }
        db.ChangeTracker.Clear();
        var after = (await queries.GetUnifiedByIdAsync(row.Id, row.UserId, Ct))!;
        Assert.Equal(before.Photos.Select(p => p.Url), after.Photos.Select(p => p.Url));
        Assert.Equal(before.DrinkPhoto.Url, after.DrinkPhoto!.Url);
        Assert.Equal(revision, after.ContentRevision);
        Assert.Equal(published, (await db.CheckIns.SingleAsync(c => c.Id == row.Id, Ct)).FirstPublishedAtUtc);
        foreach (var photo in after.Photos.Append(after.DrinkPhoto))
        {
            Assert.Equal(6, photo.Variants!.Length);
            Assert.All(photo.Variants, v =>
            {
                Assert.Equal(2, v.RecipeVersion);
                Assert.StartsWith(photo.Url + "/variants/", v.Url);
                Assert.Equal(CheckInPhotoVariantIdentity.SourceVersion(photo.Id, photo.StorageKey), v.SourceVersion);
                Assert.DoesNotContain("minio", v.Url);
            });
        }
        var home = (await queries.GetHomeByIdAsync(row.Id, row.UserId, Ct))!;
        Assert.Equal(6, home.DrinkPhoto!.Variants!.Length);
        var homePage = await queries.GetHomeByUserAsync(row.UserId, 1, 10, ct: Ct);
        Assert.Equal(6, homePage.Items.Single().Photos[0].Variants!.Length);
        var disabled = await new CheckInQueries(db, Visibility()).GetUnifiedByIdAsync(row.Id, row.UserId, Ct);
        Assert.All(disabled!.Photos, p => Assert.Null(p.Variants));
        Assert.DoesNotContain("variants", JsonSerializer.Serialize(disabled, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task WrongBindingAndLateReadyAfterDeletionDoNotPopulateProjection()
    {
        RequireLive(); await using var db = fixture.Context(false);
        var row = await Seed(db, 1); var photo = row.ShopPhotos.First();
        var projection = new CheckInPhotoVariantProjection(db); var ready = Ready(row, photo, 1);
        foreach (var invalidBinding in new[]
        {
            ready with { OwnerId = Guid.NewGuid() }, ready with { CheckInId = Guid.NewGuid() },
            ready with { SourceVersion = new string('b', 64) }, ready with { PhotoId = Guid.NewGuid() }
        }) await projection.ApplyAsync(invalidBinding, Ct);
        Assert.False(await db.CheckInPhotoVariantManifests.AnyAsync(m => m.PhotoId == photo.Id, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => projection.ApplyAsync(ready with { Variants = ready.Variants[..5] }, Ct));
        row.Delete(); await db.SaveChangesAsync(Ct);
        await projection.ApplyAsync(ready, Ct);
        Assert.False(await db.CheckInPhotoVariantManifests.AnyAsync(m => m.PhotoId == photo.Id, Ct));
        Assert.Null(await new CheckInQueries(db, Visibility(), Enabled).GetUnifiedByIdAsync(row.Id, row.UserId, Ct));
    }

    [Fact]
    public async Task FeedManifestQueryCountIsConstantAndLostProjectionCanBeRebuiltIdempotently()
    {
        RequireLive(); await using var writer = fixture.Context(false);
        var owner = Guid.NewGuid();
        var rows = new[] { await Seed(writer, 1, owner), await Seed(writer, 5, owner) };
        var messages = rows.SelectMany(r => r.ShopPhotos.Select(p => Ready(r, p, 1))).ToArray();
        var projection = new CheckInPhotoVariantProjection(writer);
        foreach (var message in messages) await projection.ApplyAsync(message, Ct);
        foreach (var size in new[] { 1, 2 })
        {
            using var probe = new ReadPerformanceCommandProbe(capturePlans: true);
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options);
            var reader = new CheckInFeedReader(db, Visibility(), Enabled);
            var page = await reader.ReadAsync(new CheckInFeedScope(null, null, owner), size, null, null, Ct);
            Assert.Equal(size, page.Items.Length);
            Assert.Single(probe.Commands, c => c.CommandText.Contains("CheckInPhotoVariantManifests"));
            Assert.Empty(db.ChangeTracker.Entries());
        }
        var photoIds = messages.Select(m => m.PhotoId).ToArray();
        await writer.CheckInPhotoVariantManifests.Where(m => photoIds.Contains(m.PhotoId)).ExecuteDeleteAsync(Ct);
        foreach (var message in messages) { await projection.ApplyAsync(message, Ct); await projection.ApplyAsync(message, Ct); }
        Assert.Equal(messages.Length, await writer.CheckInPhotoVariantManifests.CountAsync(m => photoIds.Contains(m.PhotoId), Ct));
    }

    private static async Task<CheckIn> Seed(ShopsDbContext db, int galleryCount, Guid? owner = null)
    {
        var now = DateTime.UtcNow.AddMinutes(-1);
        var row = CheckIn.CreateHome(owner ?? Guid.NewGuid(), "photo-author", null, now, DateOnly.FromDateTime(now),
            null, null, null, null, null, null);
        row.AddPhotos(Enumerable.Range(0, galleryCount).Select(i =>
            new CheckInPhoto("source.jpg", "image/jpeg", "private/" + Guid.NewGuid(), 12345, row.UserId, i)).ToArray());
        row.AddDrinkPhoto(new CheckInPhoto("cup.png", "image/png", "private/" + Guid.NewGuid(), 12000, row.UserId));
        row.ChangeVisibility(CheckInVisibility.Public);
        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow);
        db.CheckIns.Add(row); await db.SaveChangesAsync(Ct); return row;
    }

    private static CheckInPhotoVariantsReadyEvent Ready(CheckIn row, CheckInPhoto photo, int recipe) =>
        new(Guid.NewGuid(), row.Id, row.UserId, photo.Id, CheckInPhotoVariantIdentity.SourceVersion(photo.Id, photo.StorageKey), recipe,
            new[] { "thumb", "feed", "detail" }.SelectMany(p => new[] { "webp", photo.ContentType == "image/png" ? "png" : "jpeg" }
                .Select(f => new CheckInPhotoVariantInfo(p, f, "image/" + f, 100, 200, 123, new string('a', 64)))).ToArray());

    private static void RequireLive() => Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
}
