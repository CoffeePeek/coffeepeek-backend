using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
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
    
    public Task<bool> AnyAsync(Guid shopId, Guid reviewId, CancellationToken ct)
    {
        return _repository.AnyAsync(r => r.CoffeeShopId == shopId && r.Id == reviewId, ct);
    }
    
    public Task<Review?> GetById(Guid reviewId, CancellationToken ct)
    {
        return _repository.FirstOrDefaultAsync(x => x.Id == reviewId, ct);
    }

    public Task<Review[]> GetByUserId(Guid userId, CancellationToken ct)
    {
        return _repository.Where(x => x.UserId == userId).OrderBy(x => x.CreatedAtUtc).ToArrayAsync(ct);
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
