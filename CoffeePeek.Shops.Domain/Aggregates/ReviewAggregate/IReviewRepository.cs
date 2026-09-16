namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public interface IReviewRepository
{
    void Add(Review review);
    Task<bool> AnyAsync(Guid shopId, Guid reviewId, CancellationToken ct);
    Task<Review?> GetById(Guid reviewId, CancellationToken ct);
    Task<ReviewHelpfulMutationResult> SetHelpfulAsync(
        Guid reviewId,
        Guid userId,
        bool isHelpful,
        CancellationToken ct);
}

public readonly record struct ReviewHelpfulMutationResult(
    bool IsHelpful,
    int HelpfulCount,
    bool Changed);
