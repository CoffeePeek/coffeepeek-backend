using CoffeePeek.Moderation.Domain.Common.Enums;

namespace CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;

public interface IShopChangeRequestRepository
{
    void Add(ShopChangeRequest request);
    Task<ShopChangeRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ShopChangeRequest?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<(IReadOnlyList<ShopChangeRequest> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, ModerationStatus? status, Guid? shopId,
        Guid? submittedByUserId, ShopChangeSection? section, CancellationToken ct = default);
}
