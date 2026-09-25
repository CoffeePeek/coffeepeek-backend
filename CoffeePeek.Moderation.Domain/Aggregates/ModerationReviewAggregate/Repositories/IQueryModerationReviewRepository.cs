using CoffeePeek.Moderation.Domain.Common.Enums;

namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public interface IQueryModerationReviewRepository
{
    Task<ModerationReview[]> GetAll(CancellationToken cancellationToken);
    Task<ModerationReview?> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<(ModerationReview[] Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        string? search,
        Guid? userId = null,
        CancellationToken cancellationToken = default);
}
