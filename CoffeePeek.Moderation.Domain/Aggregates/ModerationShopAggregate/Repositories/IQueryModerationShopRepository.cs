using CoffeePeek.Moderation.Domain.Aggregates.Enums;
using CoffeePeek.Moderation.Domain.Common.Enums;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public interface IQueryModerationShopRepository
{
    Task<ModerationShop?> GetByPublishedShopId(Guid publishedShopId, CancellationToken cancellationToken);
    Task<ModerationShop?> GetById(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ModerationShop>> GetAllForReviewAsync(CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ModerationShop> Items, int TotalCount)> GetPagedForReviewAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        string? search,
        CoffeeFocus? coffeeFocus,
        CancellationToken cancellationToken = default);
}
