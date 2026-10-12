using CoffeePeek.Moderation.Domain.Aggregates.Enums;
using CoffeePeek.Moderation.Domain.Common.Enums;

using CoffeePeek.Moderation.Domain.Aggregates;

namespace CoffeePeek.Moderation.Application.Abstractions;

public interface IQueryModerationShopRepository
{
    Task<ModerationShop?> GetById(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<ModerationShop> Items, int TotalCount)> GetPagedForReviewAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        string? search,
        CoffeeFocus? coffeeFocus,
        Guid? userId = null,
        CancellationToken cancellationToken = default);
}
