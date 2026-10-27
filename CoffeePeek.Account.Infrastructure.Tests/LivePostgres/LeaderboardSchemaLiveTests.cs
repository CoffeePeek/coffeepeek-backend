using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class LeaderboardSchemaLiveTests
{
    [Fact]
    public async Task ShopsMigrationPersistsLocationAndGuardsConcurrentEligibilityWrites()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connection = await LivePostgresServer.CreateDatabaseAsync("leaderboardshops", ct);
        try
        {
            var options = new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options;
            await using var db = new ShopsDbContext(options);
            await db.Database.MigrateAsync(ct);
            var city = new City("Leaderboard city");
            var shop = new CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop(
                null, "Leaderboard cafe", null, PriceRange.Moderate, Guid.NewGuid());
            shop.SetLocation(city.Id, "Street 1", 53.9m, 27.5m);
            var now = DateTime.UtcNow;
            var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", "visit", null, now, CheckInVisibility.Public);
            checkIn.VerifyLocation(53.9, 27.5, 5, now, 53.9, 27.5, now);
            db.AddRange(city, shop, checkIn); await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            var first = await db.CheckIns.SingleAsync(ct);
            Assert.Equal("WithinRange", first.LeaderboardGeoStatus);
            Assert.Equal(53.9, first.DeviceLatitude);
            await using var concurrent = new ShopsDbContext(options);
            var stale = await concurrent.CheckIns.SingleAsync(ct);
            first.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, now);
            await db.SaveChangesAsync(ct);
            stale.ChangeVisibility(CheckInVisibility.Private);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => concurrent.SaveChangesAsync(ct));
            db.ChangeTracker.Clear();
            var restored = await db.CheckIns.SingleAsync(ct);
            Assert.True(restored.CountsForLeaderboard);
            Assert.Equal(first.LeaderboardVersion, restored.LeaderboardVersion);
            Assert.NotNull(restored.LeaderboardCreditedAtUtc);
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    [Fact]
    public async Task ModerationMigrationPersistsOverrideReasonAndGeoSnapshot()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connection = await LivePostgresServer.CreateDatabaseAsync("leaderboardmod", ct);
        try
        {
            var options = new DbContextOptionsBuilder<ModerationDbContext>().UseNpgsql(connection).Options;
            await using var db = new ModerationDbContext(options);
            await db.Database.MigrateAsync(ct);
            var review = ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
                "user", "", null, null, null, null, DateTime.UtcNow, [], leaderboardGeoStatus: "OutsideRadius", shopDistanceMeters: 60);
            review.SetLeaderboardDecision(true, "GPS error confirmed", Guid.NewGuid());
            db.Add(review); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            var restored = await db.ModerationReviews.SingleAsync(ct);
            Assert.True(restored.LeaderboardOverride);
            Assert.Equal("GPS error confirmed", restored.LeaderboardReason);
            Assert.Equal(60, restored.ShopDistanceMeters);
            Assert.Equal(2, restored.DecisionSequence);
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }
}
