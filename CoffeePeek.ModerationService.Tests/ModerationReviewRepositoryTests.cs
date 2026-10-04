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
    public async Task ReplacePhotos_PersistsRetainedIdentityAndNewMetadataOnReload()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ModerationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var owner = Guid.NewGuid();
        var keep = PhotoMetadata.Create("keep.jpg", "image/jpeg", "keep", 100, owner, null);
        var remove = PhotoMetadata.Create("remove.jpg", "image/jpeg", "remove", 100, owner, null);
        var review = ModerationReview.Create(owner, Guid.NewGuid(), null, "user", null, "A sufficiently long review comment", 5, 5, 5, [keep, remove]);
        await using (var seed = new ModerationDbContext(options))
        {
            seed.ModerationReviews.Add(review);
            await seed.SaveChangesAsync(ct);
        }
        var added = PhotoMetadata.Create("trusted.png", "image/png", "new", 200, owner, null);
        await using (var edit = new ModerationDbContext(options))
        {
            var loaded = (await new ModerationReviewRepository(edit).GetById(review.Id, ct))!;
            loaded.ReplacePhotos([loaded.ReviewPhotos.Single(p => p.Id == keep.Id), added]);
            new ModerationReviewRepository(edit).AddPhotos([added]);
            await edit.SaveChangesAsync(ct);
        }
        await using var read = new ModerationDbContext(options);
        var reloaded = (await new ModerationReviewRepository(read).GetById(review.Id, ct))!;
        reloaded.ReviewPhotos.Select(p => p.Id).Should().BeEquivalentTo(new[] { keep.Id, added.Id });
        reloaded.ReviewPhotos.Should().OnlyContain(p => p.OwnerId == owner && p.ModerationShopId == null);
        reloaded.ReviewPhotos.Single(p => p.Id == added.Id).SizeBytes.Should().Be(200);
    }

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
