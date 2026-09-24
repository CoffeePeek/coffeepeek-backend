using CoffeePeek.Moderation.Domain.Entities;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffeeShop.Moderation.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeePeek.ModerationService.Tests;

public class ModerationReviewRepositoryTests
{
    [Fact]
    public async Task GetById_LoadsReviewPhotos()
    {
        // Regression: approve mapped this aggregate into ModerationReviewApprovedEvent without photos,
        // so every published review in Shops came out with "photos": [].
        var options = new DbContextOptionsBuilder<ModerationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var userId = Guid.NewGuid();
        var review = ModerationReview.Create(
            userId, Guid.NewGuid(), null, "user", "Nice header", "A long enough review comment",
            4, 4, 4,
            [
                PhotoMetadata.Create("a.jpg", "image/jpeg", "a.jpg", 10, userId, moderationShopId: null),
                PhotoMetadata.Create("b.jpg", "image/jpeg", "b.jpg", 20, userId, moderationShopId: null)
            ]);

        await using (var seed = new ModerationDbContext(options))
        {
            seed.ModerationReviews.Add(review);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var db = new ModerationDbContext(options);
        var loaded = await new ModerationReviewRepository(db).GetById(review.Id, TestContext.Current.CancellationToken);

        loaded!.ReviewPhotos.Select(p => p.StorageKey).Should().BeEquivalentTo("a.jpg", "b.jpg");
    }
}
