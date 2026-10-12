using CoffeePeek.Moderation.Domain.Common.Enums;

using CoffeePeek.Moderation.Domain.Aggregates;

namespace CoffeePeek.Moderation.Application.Abstractions;

public interface IQueryModerationRoasterRepository
{
    Task<ModerationRoaster?> GetById(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<ModerationRoaster> Items, int TotalCount)> GetPagedForReviewAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        Guid? userId = null,
        CancellationToken ct = default);
}
