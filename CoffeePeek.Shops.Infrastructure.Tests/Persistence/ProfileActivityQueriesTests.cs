using CoffeePeek.Shops.Application.Features.Profiles;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ProfileActivityQueriesTests
{
    [Fact]
    public async Task ExcludesPrivatePendingRejectedDeletedStaleHiddenAndForeignCheckIns()
    {
        await using var db = Database();
        var author = Guid.NewGuid();
        var visibleShop = Shop();
        var hiddenShop = Shop();
        hiddenShop.SetHidden(true);
        db.Shops.AddRange(visibleShop, hiddenShop);
        var visit = Utc(2025, 9, 5);
        var approved = Published(author, visibleShop.Id, visit);
        var privateVisit = CheckIn.CreateUnified(author, visibleShop.Id, "author", null, null, visit);
        var pending = CheckIn.CreateUnified(author, visibleShop.Id, "author", null, null, visit, CheckInVisibility.Public);
        var rejected = CheckIn.CreateUnified(author, visibleShop.Id, "author", null, null, visit, CheckInVisibility.Public);
        rejected.ApplyModerationDecision(rejected.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Rejected, "Rejected", DateTime.UtcNow);
        var deleted = Published(author, visibleShop.Id, visit);
        deleted.Delete();
        var stale = Published(author, visibleShop.Id, visit);
        db.CheckIns.AddRange(approved, Published(author, visibleShop.Id, visit.AddDays(1)), privateVisit, pending,
            rejected, deleted, stale, Published(author, hiddenShop.Id, visit), Published(Guid.NewGuid(), visibleShop.Id, visit));
        db.Entry(stale).Property(checkIn => checkIn.ApprovedRevision).CurrentValue = stale.ContentRevision - 1;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new ProfileActivityQueries(db).GetAsync(author, ProfileActivityMonth.Parse(2025, 9),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalCheckIns);
        Assert.Equal(2, result.ActiveDays);
        Assert.Equal(30, result.Days.Length);
        Assert.Equal(1, result.Days.Single(day => day.Date == new DateOnly(2025, 9, 5)).CheckInCount);
        Assert.Equal(0, result.Days[0].CheckInCount);
    }

    [Fact]
    public async Task UsesLocalMonthBoundariesAndCountsByVisitTime()
    {
        await using var db = Database();
        var author = Guid.NewGuid();
        var shop = Shop();
        db.Shops.Add(shop);
        var visits = new[]
        {
            Utc(2025, 8, 31, 20, 59, 59), // Before September in Minsk.
            Utc(2025, 8, 31, 21),
            Utc(2025, 9, 1),
            Utc(2025, 9, 30, 20, 59, 59),
            Utc(2025, 9, 30, 21) // October in Minsk.
        };
        db.CheckIns.AddRange(visits.Select(visit => Published(author, shop.Id, visit)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new ProfileActivityQueries(db).GetAsync(author,
            ProfileActivityMonth.Parse(2025, 9, "Europe/Minsk"), TestContext.Current.CancellationToken);

        Assert.Equal("Europe/Minsk", result.TimeZone);
        Assert.Equal(3, result.TotalCheckIns);
        Assert.Equal(2, result.ActiveDays);
        Assert.Equal(2, result.Days[0].CheckInCount);
        Assert.Equal(1, result.Days[^1].CheckInCount);
    }

    [Fact]
    public async Task RepeatedHourCountsOnTheSameLocalDay()
    {
        await using var db = Database();
        var author = Guid.NewGuid();
        var shop = Shop();
        db.Shops.Add(shop);
        db.CheckIns.AddRange(Published(author, shop.Id, Utc(2025, 11, 2, 5, 30)),
            Published(author, shop.Id, Utc(2025, 11, 2, 6, 30)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new ProfileActivityQueries(db).GetAsync(author,
            ProfileActivityMonth.Parse(2025, 11, "America/New_York"), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ActiveDays);
        Assert.Equal(2, result.TotalCheckIns);
        Assert.Equal(2, result.Days[1].CheckInCount);
    }

    [Fact]
    public async Task CountsApprovedHomeAndShopCheckInsTogether_WithoutRequiringAShopForHome()
    {
        await using var db = Database();
        var author = Guid.NewGuid();
        var visit = Utc(2025, 9, 5, 10);
        var visibleShop = Shop();
        var hiddenShop = Shop();
        hiddenShop.SetHidden(true);
        db.Shops.AddRange(visibleShop, hiddenShop);
        var home = CheckIn.CreateHome(author, "author", "Home coffee", visit,
            DateOnly.FromDateTime(visit), null, null, null, null, null, null,
            visibility: CheckInVisibility.Public);
        home.ApplyModerationDecision(home.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow);
        var privateHome = CheckIn.CreateHome(author, "author", null, visit.AddDays(1),
            DateOnly.FromDateTime(visit.AddDays(1)), null, null, null, null, null, null);
        db.CheckIns.AddRange(home, privateHome, Published(author, visibleShop.Id, visit),
            Published(author, hiddenShop.Id, visit.AddDays(2)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new ProfileActivityQueries(db).GetAsync(author, ProfileActivityMonth.Parse(2025, 9),
            TestContext.Current.CancellationToken);

        Assert.Null(home.ShopId);
        Assert.Equal(CheckInKind.Home, home.Kind);
        Assert.Equal(2, result.TotalCheckIns);
        Assert.Equal(1, result.ActiveDays);
        Assert.Equal(2, result.Days.Single(day => day.Date == new DateOnly(2025, 9, 5)).CheckInCount);
        Assert.Equal(0, result.Days.Single(day => day.Date == new DateOnly(2025, 9, 6)).CheckInCount);
    }

    [Fact]
    public async Task EmptyLeapMonthIncludesEveryDayWithZeroCounts()
    {
        await using var db = Database();
        var result = await new ProfileActivityQueries(db).GetAsync(Guid.NewGuid(), ProfileActivityMonth.Parse(2024, 2),
            TestContext.Current.CancellationToken);

        Assert.Equal(29, result.Days.Length);
        Assert.Equal(0, result.ActiveDays);
        Assert.Equal(0, result.TotalCheckIns);
        Assert.All(result.Days, day => Assert.Equal(0, day.CheckInCount));
    }

    [Fact]
    public void VisitSelectionTranslatesToBoundedPostgresSql()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=disposable;Username=test;Password=test").Options);
        var sql = new ProfileActivityQueries(db).VisitsInMonth(Guid.NewGuid(), ProfileActivityMonth.Parse(2025, 9)).ToQueryString();
        Assert.Contains("ApprovedRevision", sql);
        Assert.Contains("ContentRevision", sql);
        Assert.Contains("DeletedAtUtc", sql);
        Assert.Contains("IsHidden", sql);
        Assert.Contains("\"VisitedAt\" >=", sql);
        Assert.Contains("\"VisitedAt\" <", sql);
        Assert.Contains("\"UserId\" =", sql);
    }

    [Theory]
    [InlineData(9, 3)]
    [InlineData(10, 2)]
    public async Task MidnightGapDoesNotLoseOrDuplicateCheckInsBetweenAdjacentMonths(int month, int expectedCount)
    {
        await using var db = Database();
        var author = Guid.NewGuid();
        var shop = Shop();
        db.Shops.Add(shop);
        db.CheckIns.AddRange(new[]
        {
            Utc(2017, 9, 1, 4),
            Utc(2017, 10, 1, 3, 30),
            Utc(2017, 10, 1, 4).AddMilliseconds(-1),
            Utc(2017, 10, 1, 4),
            Utc(2017, 10, 31, 12),
            Utc(2017, 11, 1, 3)
        }.Select(visit => Published(author, shop.Id, visit)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new ProfileActivityQueries(db).GetAsync(author,
            ProfileActivityMonth.Parse(2017, month, "America/Asuncion"), TestContext.Current.CancellationToken);

        Assert.Equal(expectedCount, result.TotalCheckIns);
        Assert.Equal(expectedCount, result.Days.Sum(day => day.CheckInCount));
        Assert.Equal(2, result.ActiveDays);
        Assert.Equal(1, result.Days[0].CheckInCount);
        Assert.Equal(expectedCount - 1, result.Days[^1].CheckInCount);
    }
    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CoffeeShop Shop() => new(null, "Calendar shop", null, PriceRange.Moderate, Guid.NewGuid());

    private static CheckIn Published(Guid author, Guid shop, DateTime visitedAt)
    {
        var row = CheckIn.CreateUnified(author, shop, "author", null, null, visitedAt, CheckInVisibility.Public);
        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow);
        return row;
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);
}
