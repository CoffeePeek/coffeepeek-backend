using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class HomeCheckInQueriesTests
{
    private static readonly DateTime September20Utc = new(2026, 9, 20, 22, 15, 0, DateTimeKind.Utc);

    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CheckIn Home(Guid userId, DateOnly preparedOn, string? method = null,
        Guid? productId = null, string? bean = null, decimal? dose = null, int? taste = null) =>
        CheckIn.CreateHome(userId, "author", null, September20Utc, preparedOn,
            method, productId, bean, dose, null, taste);

    [Fact]
    public async Task HomeHistoryCalendarAndPhotoReadAreOwnerScopedByLocalDate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var author = Guid.NewGuid();
        var other = Guid.NewGuid();
        var day = new DateOnly(2026, 9, 21);
        var home = Home(author, day, "batch_drip", bean: "House beans", dose: 75m);
        home.AddPhotos([new CheckInPhoto("batch.jpg", "image/jpeg", "private/batch.jpg", 100, author)]);
        var otherHome = Home(other, day, "espresso");
        var deleted = Home(author, day, "espresso");
        deleted.Delete();
        db.CheckIns.AddRange(home, otherHome, deleted);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var queries = new CheckInQueries(db, Moq.Mock.Of<CoffeePeek.Shops.Application.Abstractions.IAuthorVisibilityLookup>());
        var history = await queries.GetHomeByUserAsync(author, 1, 20, day, day, ct: ct);
        Assert.Equal(1, history.TotalCount);
        var item = Assert.Single(history.Items);
        Assert.Equal(home.Id, item.Id);
        Assert.Equal(day, item.PreparedOn);
        Assert.Equal(September20Utc, item.PreparedAtUtc);
        Assert.Null(await queries.GetHomeByIdAsync(home.Id, other, ct));
        Assert.Equal(home.Id, (await queries.GetHomeByIdAsync(home.Id, author, ct))!.Id);
        Assert.Equal($"/api/v1/check-ins/{home.Id}/photos/{item.Photos.Single().Id}", item.Photos.Single().Url);

        var calendar = await queries.GetHomeCalendarAsync(author, day, day, ct);
        Assert.Equal(day, Assert.Single(calendar).PreparedOn);
        Assert.Equal(1, calendar[0].BrewCount);
        Assert.Empty(await queries.GetHomeCalendarAsync(other, day.AddDays(1), day.AddDays(1), ct));
    }

    [Fact]
    public async Task PrivateHomeRowsStayOutOfPublicFeedsAndShopRatingAggregates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var shop = new CoffeeShop(null, "Coffee shop", null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "Address", 53m, 27m);
        db.Shops.Add(shop);
        var author = Guid.NewGuid();
        var shopVisit = CheckIn.CreateUnified(author, shop.Id, "author", "visit", Rating.Create(4, 4, 4),
            September20Utc, CheckInVisibility.Public);
        shopVisit.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved,
            null, September20Utc);
        var home = Home(author, new DateOnly(2026, 9, 21), "espresso", taste: 5);
        db.CheckIns.AddRange(shopVisit, home);
        await db.SaveChangesAsync(ct);

        var queries = new CheckInQueries(db, Moq.Mock.Of<CoffeePeek.Shops.Application.Abstractions.IAuthorVisibilityLookup>());
        Assert.Null(await queries.GetUnifiedByIdAsync(home.Id, Guid.NewGuid(), ct));
        Assert.Equal(home.Id, (await queries.GetUnifiedByIdAsync(home.Id, author, ct))!.Id);
        Assert.Equal(shopVisit.Id, Assert.Single((await queries.GetPublicAsync(null, null, null,
            null, 20, null, ct)).Items).Id);
        Assert.Equal((4m, 1), await queries.GetStatsByShopAsync(shop.Id, ct));
    }

    [Fact]
    public async Task BeanAndMethodStatisticsIgnoreMissingDoseAndScoreInsteadOfCountingThemAsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var author = Guid.NewGuid();
        var product = Guid.NewGuid();
        var day = new DateOnly(2026, 9, 21);
        var first = Home(author, day, "espresso", product, "Original label", 18m, 5);
        var second = Home(author, day, "espresso", product, "Updated catalog label", null, null);
        var custom = Home(author, day, "batch_drip", bean: "My beans", dose: 75m, taste: 3);
        var sameCustom = Home(author, day, method: null, bean: "my beans");
        var unknown = Home(author, day);
        var deleted = Home(author, day, "espresso", product, "Original label", 100m, 1);
        deleted.Delete();
        db.CheckIns.AddRange(first, second, custom, sameCustom, unknown, deleted,
            Home(Guid.NewGuid(), day, "espresso", product, "Original label", 100m, 1));
        await db.SaveChangesAsync(ct);

        var queries = new CheckInQueries(db, Moq.Mock.Of<CoffeePeek.Shops.Application.Abstractions.IAuthorVisibilityLookup>());
        Assert.Equal("Original label", (await queries.GetHomeByIdAsync(first.Id, author, ct))!.BeanName);
        Assert.Equal("Updated catalog label", (await queries.GetHomeByIdAsync(second.Id, author, ct))!.BeanName);
        var beans = await queries.GetHomeBeanStatsAsync(author, ct);
        var linked = Assert.Single(beans, bean => bean.CoffeeProductId == product);
        Assert.Equal(2, linked.BrewCount);
        Assert.Equal(1, linked.DosedBrewCount);
        Assert.Equal(18m, linked.TotalKnownDoseGrams);
        Assert.Equal(1, linked.RatedBrewCount);
        Assert.Equal(5m, linked.AverageTasteScore);
        var customStats = Assert.Single(beans, bean => bean.BeanName?.Equals("My beans", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal(2, customStats.BrewCount);
        Assert.Equal(1, customStats.DosedBrewCount);
        Assert.Equal(75m, customStats.TotalKnownDoseGrams);
        Assert.Equal(3m, customStats.AverageTasteScore);
        var unknownStats = Assert.Single(beans, bean => bean.CoffeeProductId is null && bean.BeanName is null);
        Assert.Equal(1, unknownStats.BrewCount);
        Assert.Equal(0, unknownStats.DosedBrewCount);
        Assert.Null(unknownStats.AverageTasteScore);

        var methods = await queries.GetHomeMethodStatsAsync(author, ct);
        var espresso = Assert.Single(methods, method => method.BrewMethodCode == "espresso");
        Assert.Equal(2, espresso.BrewCount);
        Assert.Equal(1, espresso.DosedBrewCount);
        Assert.Equal(18m, espresso.TotalKnownDoseGrams);
        Assert.Equal(5m, espresso.AverageTasteScore);
        Assert.Equal(2, Assert.Single(methods, method => method.BrewMethodCode is null).BrewCount);
    }
}
