using CoffeePeek.Moderation.Domain.Common.Enums;

using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

namespace CoffeePeek.Moderation.Application.Abstractions;

public interface IQueryModerationReviewRepository
{
    Task<ModerationReview?> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<(ModerationReview[] Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        string? search,
        Guid? userId = null,
        CancellationToken cancellationToken = default);
}
