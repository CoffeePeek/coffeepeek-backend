namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public interface IModerationReviewRepository
{
    void AddPhotos(IEnumerable<CoffeePeek.Moderation.Domain.Entities.PhotoMetadata> photos);
    void Add(ModerationReview review);
    Task<ModerationReview?> GetById(Guid id, CancellationToken ct = default);
    Task<ModerationReview?> GetByShopId(Guid shopId, CancellationToken ct = default);
}