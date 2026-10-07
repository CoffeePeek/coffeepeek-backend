namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public interface IModerationReviewRepository
{
    void Add(ModerationReview review);
    Task<ModerationReview?> GetById(Guid id, CancellationToken ct = default);
    Task<ModerationReview?> GetByCheckInRevision(Guid checkInId, int contentRevision, CancellationToken ct = default);
}
