using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class AdminStatsQueryRepositoryTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private static DateTime Since => DateTime.UtcNow.AddDays(-30);

    private static ShopsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CheckIn Review(Guid shopId, int place, int service, int coffee)
    {
        var c = CheckIn.CreateUnified(Guid.NewGuid(), shopId, "author", "visit", Rating.Create(place,service,coffee), DateTime.UtcNow, CheckInVisibility.Public);
        c.ApplyModerationDecision(1,Guid.NewGuid(),1,CheckInModerationState.Approved,null,DateTime.UtcNow);
        return c;
    }

    private static CoffeeShop Shop(string name) => new(Guid.NewGuid(), name, null, PriceRange.Moderate, Guid.NewGuid());

    private static CheckIn CheckInAt(Guid shopId, DateTime createdAtUtc)
    {
        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), shopId, "author", "visit", Rating.Create(4,4,4), createdAtUtc);
        checkIn.CreatedAtUtc = createdAtUtc;
        return checkIn;
    }

    [Fact]
    public async Task GetInsightsAsync_RatingsDistributionAndAverages_IgnoreSoftDeleted()
    {
        await using var db = CreateContext();
        var shopId = Guid.NewGuid();
        var visibleShop=Shop("fixture");db.Shops.Add(visibleShop);db.Entry(visibleShop).Property(x=>x.Id).CurrentValue=shopId;
        db.CheckIns.Add(Review(shopId, 5, 5, 4));
        db.CheckIns.Add(Review(shopId, 1, 1, 1));
        db.CheckIns.Add(Review(shopId, 3, 3, 3));
        var deleted = Review(shopId, 5, 5, 5);
        deleted.Delete();
        db.CheckIns.Add(deleted);
        await db.SaveChangesAsync(_ct);

        var ratings = (await new AdminStatsQueryRepository(db).GetInsightsAsync(Since, _ct)).Ratings;

        ratings.TotalCheckIns.Should().Be(3);
        ratings.StarCounts.Should().Equal((1, 1), (2, 0), (3, 1), (4, 0), (5, 1));
        Math.Round(ratings.AverageRating, 2, MidpointRounding.AwayFromZero).Should().Be(2.89m);
        Math.Round(ratings.AveragePlace, 2, MidpointRounding.AwayFromZero).Should().Be(3m);
        Math.Round(ratings.AverageCoffee, 2, MidpointRounding.AwayFromZero).Should().Be(2.67m);
    }

    [Fact]
    public async Task GetInsightsAsync_NoReviews_ReturnsFiveEmptyBucketsAndZeroAverages()
    {
        await using var db = CreateContext();

        var ratings = (await new AdminStatsQueryRepository(db).GetInsightsAsync(Since, _ct)).Ratings;

        ratings.TotalCheckIns.Should().Be(0);
        ratings.AverageRating.Should().Be(0m);
        ratings.AveragePlace.Should().Be(0m);
        ratings.StarCounts.Select(x => x.Stars).Should().Equal(1, 2, 3, 4, 5);
        ratings.StarCounts.Should().OnlyContain(x => x.Count == 0);
    }

    [Fact]
    public async Task GetInsightsAsync_TopShopsByCheckIns_CountsLast30DaysOnly()
    {
        await using var db = CreateContext();
        var a = Shop("Alpha");
        var b = Shop("Beta");
        db.Shops.AddRange(a, b);
        var now = DateTime.UtcNow;
        db.CheckIns.AddRange(
            CheckInAt(a.Id, now.AddDays(-1)),
            CheckInAt(a.Id, now.AddDays(-5)),
            CheckInAt(a.Id, now.AddDays(-29)),
            CheckInAt(a.Id, now.AddDays(-45)),
            CheckInAt(b.Id, now.AddHours(-3)),
            CheckInAt(Guid.NewGuid(), now)); // shop no longer exists -> dropped
        await db.SaveChangesAsync(_ct);

        var top = (await new AdminStatsQueryRepository(db).GetInsightsAsync(Since, _ct)).TopShopsByCheckIns;

        top.Should().Equal(new AdminTopShop(a.Id, "Alpha", 3), new AdminTopShop(b.Id, "Beta", 1));
    }

    [Fact]
    public async Task GetInsightsAsync_TopShopsByCheckIns_LimitedToTen()
    {
        await using var db = CreateContext();
        for (var i = 0; i < 12; i++)
        {
            var shop = Shop($"Shop {i}");
            db.Shops.Add(shop);
            db.CheckIns.Add(CheckInAt(shop.Id, DateTime.UtcNow));
        }
        await db.SaveChangesAsync(_ct);

        var top = (await new AdminStatsQueryRepository(db).GetInsightsAsync(Since, _ct)).TopShopsByCheckIns;

        top.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetInsightsAsync_Downloads_TotalsChannelsAndCountries()
    {
        await using var db = CreateContext();
        var androidDe = AppDownloadRedirectEvent.Create("android", "ua", null, "DE");
        var androidUnknown = AppDownloadRedirectEvent.Create("android", "ua", null, null);
        var iosOld = AppDownloadRedirectEvent.Create("ios", "ua", null, "US");
        db.AppDownloadRedirectEvents.AddRange(androidDe, androidUnknown, iosOld);
        db.Entry(iosOld).Property(x => x.Timestamp).CurrentValue = DateTime.UtcNow.AddDays(-40);
        await db.SaveChangesAsync(_ct);

        var downloads = (await new AdminStatsQueryRepository(db).GetInsightsAsync(Since, _ct)).Downloads;

        downloads.Total.Should().Be(3);
        downloads.SinceCount.Should().Be(2);
        downloads.ByChannel.Should().Equal(("android", 2, 2), ("ios", 1, 0));
        downloads.TopCountriesSince.Should().BeEquivalentTo(new[] { ("DE", 1), ("unknown", 1) });
    }

    [Fact]
    public async Task GetDailyCountsAsync_GroupsByUtcDay_ExcludesDeletedReviewsAndOldRows()
    {
        await using var db = CreateContext();
        var today = DateTime.UtcNow.Date;
        var from = today.AddDays(-3);

        var shopToday = Shop("Today");
        shopToday.CreatedAtUtc = today.AddHours(2);
        var shopOld = Shop("Old");
        shopOld.CreatedAtUtc = today.AddDays(-10);
        db.Shops.AddRange(shopToday, shopOld);

        var review1 = Review(shopToday.Id, 3, 3, 3);
        review1.CreatedAtUtc = today.AddDays(-1).AddHours(1);
        var review2 = Review(shopToday.Id, 3, 3, 3);
        review2.CreatedAtUtc = today.AddDays(-1).AddHours(22);
        var deletedReview = Review(shopToday.Id, 3, 3, 3);
        deletedReview.CreatedAtUtc = today.AddDays(-1);
        deletedReview.Delete();
        db.CheckIns.AddRange(review1, review2, deletedReview);

        db.CheckIns.AddRange(
            CheckInAt(shopToday.Id, today.AddDays(-2).AddHours(8)),
            CheckInAt(shopToday.Id, today.AddDays(-20)));
        await db.SaveChangesAsync(_ct);

        var counts = await new AdminStatsQueryRepository(db).GetDailyCountsAsync(from, _ct);

        counts.Shops.Should().BeEquivalentTo(new Dictionary<DateOnly, int> { [DateOnly.FromDateTime(today)] = 1 });
        counts.CheckIns.Should().BeEquivalentTo(new Dictionary<DateOnly, int>
        {
            [DateOnly.FromDateTime(today.AddDays(-2))] = 1,
            [DateOnly.FromDateTime(today.AddDays(-1))] = 2
        });
    }
}
