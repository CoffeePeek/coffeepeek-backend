using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class UnifiedCheckInQueriesTests
{
    [Fact]
    public async Task CutoutSurvivesPersistenceAndAppearsInCalendarAndFeedWithoutLeakingPrivateVisits()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var shop = new CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop(null, "shop", null,
            CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Moderate, Guid.NewGuid());
        db.Shops.Add(shop);
        var row = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", null, null, DateTime.UtcNow.AddDays(-1));
        var original = new ShopPhoto("full.jpg", "image/jpeg", "private/full", 10, row.UserId);
        var cup = new ShopPhoto("cup.png", "image/png", "private/cup", 10, row.UserId);
        row.AddPhotos([original]); row.AddDrinkPhoto(cup); db.CheckIns.Add(row);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        row = await db.CheckIns.Include(c => c.ShopPhotos).SingleAsync();
        Assert.Equal(cup.Id, row.DrinkPhotoId);
        var queries = new CheckInQueries(db);
        var calendar = Assert.Single((await queries.GetUnifiedByUserAsync(row.UserId, 1, 10)).Items);
        Assert.Equal(original.Id, Assert.Single(calendar.Photos).Id);
        Assert.Equal(cup.Id, calendar.DrinkPhoto!.Id);
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, null));
        row.ChangeVisibility(CheckInVisibility.Public);
        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var feed = await new CheckInFeedReader(db).ReadAsync(new(), 20, null, null, default);
        Assert.Equal(calendar.DrinkPhoto, Assert.Single(feed.Items).CheckIn.DrinkPhoto);
        Assert.Equal(calendar.DrinkPhoto, Assert.Single((await queries.GetPublicAsync(null, null, shop.Id, row.UserId, 20, null)).Items).DrinkPhoto);
        row.ChangeVisibility(CheckInVisibility.Private); await db.SaveChangesAsync();
        Assert.Empty((await new CheckInFeedReader(db).ReadAsync(new(), 20, null, null, default)).Items);
        row.Delete(); await db.SaveChangesAsync();
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, row.UserId));
    }

    [Fact]
    public async Task PrivateForeignAndDeletedEntriesAreNotReadable()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var row = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", "x", Rating.Create(1, 2, 3), DateTime.UtcNow);
        db.CheckIns.Add(row);
        await db.SaveChangesAsync();
        var queries = new CheckInQueries(db);
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, Guid.NewGuid()));
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, null));
        Assert.Equal(row.Id, (await queries.GetUnifiedByIdAsync(row.Id, row.UserId))!.Id);
        row.Delete();
        await db.SaveChangesAsync();
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, row.UserId));
        Assert.Empty((await queries.GetUnifiedByUserAsync(row.UserId, 1, 10)).Items);
    }

    [Fact]
    public void ModelKeepsRowVersionSeparateAndPublicSqlIsTranslatable()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=disposable;Username=test;Password=test").Options);
        var entity = db.Model.FindEntityType(typeof(CheckIn))!;
        Assert.True(entity.FindProperty(nameof(CheckIn.RowVersion))!.IsConcurrencyToken);
        Assert.False(entity.FindProperty(nameof(CheckIn.ContentRevision))!.IsConcurrencyToken);
        var sql = CheckInEligibility.LatestRatings(db).ToQueryString();
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("ApprovedRevision", sql);
        Assert.Contains("VisitedAt", sql);
    }
}
