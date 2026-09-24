using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public interface IReviewRepository
{
    void Add(Review review);
    void TrackPhotoReplacement(IReadOnlyCollection<ShopPhoto> removed, IReadOnlyCollection<ShopPhoto> added);
    Task<bool> AnyAsync(Guid shopId, Guid reviewId, CancellationToken ct);
    Task<Review?> GetById(Guid reviewId, CancellationToken ct);
    Task<Review?> GetByModerationReviewId(Guid moderationReviewId, CancellationToken ct);
    Task<Review?> GetUnlinkedByShopAndUser(Guid shopId, Guid userId, CancellationToken ct);
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
