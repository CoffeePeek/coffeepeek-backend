using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class ReviewRepository(ShopsDbContext dbContext) : IReviewRepository
{
    private readonly DbSet<Review> _repository = dbContext.Reviews;
    
    public void Add(Review review)
    {
        _repository.Add(review);
    }

    // Both calls are deliberately explicit:
    // a) ShopPhoto has three optional parent FKs (CoffeeShopId, CheckInId, ReviewId) on one shared
    //    table, so a required/cascade relationship is impossible. Removing an item from
    //    Review.Photos would only null ReviewId and leave an orphan row -> RemoveRange deletes it.
    // b) ShopPhoto.Id is assigned client-side while EF treats it as ValueGeneratedOnAdd. On a
    //    tracked parent, DetectChanges would classify a new photo with a non-default key as
    //    Modified and the UPDATE would hit 0 rows (same bug as the RoasterPhoto 409). AddRange
    //    marks them Added; DetectChanges still fixes up the ReviewId shadow FK from the navigation.
    // Changing the ShopPhoto key config instead would alter the snapshot and require a migration.
    public void TrackPhotoReplacement(IReadOnlyCollection<ShopPhoto> removed, IReadOnlyCollection<ShopPhoto> added)
    {
        dbContext.ShopPhotos.RemoveRange(removed);
        dbContext.ShopPhotos.AddRange(added);
    }

    public Task<bool> AnyAsync(Guid shopId, Guid reviewId, CancellationToken ct)
    {
        return _repository.AnyAsync(r => r.CoffeeShopId == shopId && r.Id == reviewId, ct);
    }
    
    public Task<Review?> GetById(Guid reviewId, CancellationToken ct)
    {
        return _repository.FirstOrDefaultAsync(x => x.Id == reviewId, ct);
    }

    // Deliberately does NOT filter IsSoftDelete: hidden (edited) rows must be found so a
    // re-approval can Restore() and update the SAME row.
    public Task<Review?> GetByModerationReviewId(Guid moderationReviewId, CancellationToken ct)
    {
        // Photos are included so ReplacePhotos sees (and can delete) the full old set.
        return _repository
            .Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.ModerationReviewId == moderationReviewId, ct);
    }

    public Task<Review?> GetUnlinkedByShopAndUser(Guid shopId, Guid userId, CancellationToken ct)
    {
        return _repository.FirstOrDefaultAsync(
            x => x.CoffeeShopId == shopId && x.UserId == userId && x.ModerationReviewId == null, ct);
    }

    public async Task<ReviewHelpfulMutationResult> SetHelpfulAsync(
        Guid reviewId,
        Guid userId,
        bool isHelpful,
        CancellationToken ct)
    {
        var changed = isHelpful
            ? await MarkHelpfulAsync(reviewId, userId, ct)
            : await UnmarkHelpfulAsync(reviewId, userId, ct);

        var state = await _repository
            .AsNoTracking()
            .Where(r => r.Id == reviewId)
            .Select(r => new
            {
                r.HelpfulCount,
                IsHelpful = dbContext.ReviewHelpfulVotes.Any(v =>
                    v.ReviewId == reviewId && v.UserId == userId)
            })
            .SingleAsync(ct);

        return new ReviewHelpfulMutationResult(state.IsHelpful, state.HelpfulCount, changed);
    }

    private async Task<bool> MarkHelpfulAsync(Guid reviewId, Guid userId, CancellationToken ct)
    {
        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            WITH inserted AS (
                INSERT INTO "ReviewHelpfulVotes" ("ReviewId", "UserId", "CreatedAtUtc")
                SELECT {reviewId}, {userId}, {DateTime.UtcNow}
                WHERE EXISTS (
                    SELECT 1
                    FROM "Reviews"
                    WHERE "Id" = {reviewId} AND NOT "IsSoftDelete"
                )
                ON CONFLICT ("ReviewId", "UserId") DO NOTHING
                RETURNING "ReviewId"
            )
            UPDATE "Reviews"
            SET "HelpfulCount" = "HelpfulCount" + 1
            WHERE "Id" = {reviewId}
              AND EXISTS (SELECT 1 FROM inserted)
            """, ct);

        return affected == 1;
    }

    private async Task<bool> UnmarkHelpfulAsync(Guid reviewId, Guid userId, CancellationToken ct)
    {
        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            WITH deleted AS (
                DELETE FROM "ReviewHelpfulVotes"
                WHERE "ReviewId" = {reviewId} AND "UserId" = {userId}
                RETURNING "ReviewId"
            )
            UPDATE "Reviews"
            SET "HelpfulCount" = GREATEST("HelpfulCount" - 1, 0)
            WHERE "Id" = {reviewId}
              AND EXISTS (SELECT 1 FROM deleted)
            """, ct);

        return affected == 1;
    }
}
