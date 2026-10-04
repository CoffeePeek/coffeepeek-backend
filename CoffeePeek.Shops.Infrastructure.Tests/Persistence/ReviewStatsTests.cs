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

    [Fact]
    public async Task ExistingReview_RequiresMatchingOwnerShopAndVisiblePublishedState()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = Guid.NewGuid();
        var shop = Guid.NewGuid();
        var visible = Make(user, shop);
        visible.CreatedAtUtc = DateTime.UtcNow.AddDays(-1);
        var deleted = Make(user, shop); deleted.SoftDelete();
        var hidden = Make(user, shop); hidden.Hide();
        var removed = Make(user, shop); removed.RemoveByAdmin();
        var anotherUser = Make(Guid.NewGuid(), shop);
        var anotherShop = Make(user, Guid.NewGuid());
        db.Reviews.AddRange(visible, deleted, hidden, removed, anotherUser, anotherShop);
        await db.SaveChangesAsync(_ct);
        db.ChangeTracker.Clear();
        var repo = new QueryReviewRepository(db);
        (await repo.GetExistingReviewIdByUserAndShopAsync(user, shop, _ct)).Should().Be(visible.Id);
        (await repo.GetExistingReviewIdByUserAndShopAsync(Guid.NewGuid(), shop, _ct)).Should().BeNull();
        (await repo.GetExistingReviewIdByUserAndShopAsync(user, Guid.NewGuid(), _ct)).Should().BeNull();
        db.ChangeTracker.Entries<Review>().Should().BeEmpty();
    }

    [Fact]
    public async Task ExistingReview_UsesNewestThenIdForLegacyDuplicatesAndNeverRequiresSourceLink()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = Guid.NewGuid();
        var shop = Guid.NewGuid();
        var old = Make(user, shop); old.CreatedAtUtc = DateTime.UtcNow.AddDays(-2);
        var first = Make(user, shop); first.CreatedAtUtc = DateTime.UtcNow.AddDays(-1);
        var second = Make(user, shop); second.CreatedAtUtc = first.CreatedAtUtc;
        typeof(Review).GetProperty("ModerationReviewId")!.SetValue(first, null);
        typeof(Review).GetProperty("ModerationReviewId")!.SetValue(second, null);
        db.Reviews.AddRange(old, first, second);
        await db.SaveChangesAsync(_ct);
        var expected = new[] { first.Id, second.Id }.OrderByDescending(id => id).First();
        (await new QueryReviewRepository(db).GetExistingReviewIdByUserAndShopAsync(user, shop, _ct))
            .Should().Be(expected);
    }

    private static Review Make(Guid user, Guid shop) => CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review.Create(
        shop, user, "tester", null, "A sufficiently long review comment", 5, 5, 5, Guid.NewGuid());

    private static Review Review(string header, Guid shopId, int place, int service, int coffee) =>
        CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review.Create(
            shopId, Guid.NewGuid(), "tester", null, "A long enough comment", place, service, coffee, Guid.NewGuid());
}
