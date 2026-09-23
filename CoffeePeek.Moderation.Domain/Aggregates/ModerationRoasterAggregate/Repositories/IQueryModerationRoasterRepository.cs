using CoffeePeek.Moderation.Domain.Common.Enums;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public interface IQueryModerationRoasterRepository
{
    Task<ModerationRoaster?> GetById(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<ModerationRoaster> Items, int TotalCount)> GetPagedForReviewAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        CancellationToken ct = default);
}
