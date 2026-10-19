using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Infrastructure.Account;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInPhotoAccessLiveTests(ShopsLiveDatabase fixture) : IClassFixture<ShopsLiveDatabase>
{
    [Fact]
    public async Task PhotoUsesOneSqlQueryAndStillObservesHideBlockAndDeletion()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var city = new City("photo-access-" + Guid.NewGuid());
        var shop = ShopsLiveDatabase.NewShop("photo access", city.Id);
        var row = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", "test", null,
            DateTime.UtcNow.AddDays(-1), CheckInVisibility.Public);
        var photos = Enumerable.Range(0, 5).Select(i => new CheckInPhoto("photo.jpg", "image/jpeg",
            "private/" + Guid.NewGuid(), 42, row.UserId, i)).ToArray();
        row.AddPhotos(photos);
        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow);
        await using var writer = fixture.Context(false);
        writer.Cities.Add(city); writer.Shops.Add(shop); writer.CheckIns.Add(row); await writer.SaveChangesAsync(ct);
        using var probe = new ReadPerformanceCommandProbe(capturePlans: true);
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options);
        var visibility = new Mock<IAuthorVisibilityLookup>();
        visibility.Setup(v => v.ExcludedUserIdsAsync(It.IsAny<Guid?>(), ct)).ReturnsAsync([]);
        using var cache = new CheckInPhotoVisibilityCache(TimeProvider.System);
        var access = new CheckInPhotoAccessQueries(db, visibility.Object, cache);
        Assert.NotNull(await access.GetAsync(row.Id, photos[2].Id, null, ct));
        Assert.Equal(1, probe.Count);
        Assert.Empty(db.ChangeTracker.Entries());
        await db.Database.OpenConnectionAsync(ct);
        using (var plan = probe.Commands.Single())
        {
            plan.Connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
            var explain = (string)(await plan.ExecuteScalarAsync(ct))!;
            Assert.Contains("CheckInPhotos", explain);
            Assert.DoesNotContain("CheckInHelpfulVotes", explain);
        }
        var reader = Guid.NewGuid();
        var beforeVariant = probe.Count;
        Assert.NotNull((await access.GetVariantAsync(row.Id, photos[2].Id, reader, ct))?.PolicyObservation);
        Assert.Equal(beforeVariant + 1, probe.Count);
        visibility.Setup(v => v.ExcludedUserIdsAsync(reader, ct)).ReturnsAsync([row.UserId]);
        Assert.Null(await access.GetAsync(row.Id, photos[2].Id, reader, ct));
        Assert.NotNull(await access.GetVariantAsync(row.Id, photos[2].Id, reader, ct)); // Warm facts, bounded to 20 seconds.
        shop.SetHidden(true); await writer.SaveChangesAsync(ct);
        Assert.Null(await access.GetVariantAsync(row.Id, photos[2].Id, reader, ct)); // Community state is never cached.
        Assert.Null(await access.GetAsync(row.Id, photos[2].Id, null, ct));
        Assert.NotNull(await access.GetAsync(row.Id, photos[2].Id, row.UserId, ct));
        row.Delete(); await writer.SaveChangesAsync(ct);
        Assert.Null(await access.GetAsync(row.Id, photos[2].Id, row.UserId, ct));
    }
}
