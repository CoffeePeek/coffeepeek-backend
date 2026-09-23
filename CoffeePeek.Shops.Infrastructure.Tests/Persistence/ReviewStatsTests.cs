using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class ReviewStatsTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShopRating_IsAverageOfAllVisibleReviews()
    {
        // Regression: the stats query returned the first review's truncated score with Count = 1.
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var shopId = Guid.NewGuid();
        db.Reviews.Add(Review("5,5,4", shopId, 5, 5, 4));
        db.Reviews.Add(Review("1,1,1", shopId, 1, 1, 1));
        var deleted = Review("5,5,5", shopId, 5, 5, 5);
        deleted.SoftDelete();
        db.Reviews.Add(deleted);
        await db.SaveChangesAsync(_ct);

        var (average, count) = await new QueryReviewRepository(db).GetReviewStatsByCoffeeShopId(shopId, _ct);

        count.Should().Be(2);
        average.Should().Be(2.83m);
    }

    [Fact]
    public async Task GetById_DoesNotReturnDeletedReview()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var review = Review("x", Guid.NewGuid(), 5, 5, 5);
        review.SoftDelete();
        db.Reviews.Add(review);
        await db.SaveChangesAsync(_ct);

        (await new QueryReviewRepository(db).GetById(review.Id, _ct)).Should().BeNull();
    }

    private static Review Review(string header, Guid shopId, int place, int service, int coffee) =>
        CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review.Create(
            shopId, Guid.NewGuid(), "tester", null, "A long enough comment", place, service, coffee, Guid.NewGuid());
}
