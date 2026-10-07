namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public interface IShopMenuRepository
{
    Task<ShopMenu?> GetTrackedByShopIdAsync(Guid shopId, CancellationToken ct = default);
    Task<ShopMenu> ApplyManualItemsAsync(
        Guid shopId,
        IReadOnlyList<ManualShopMenuItemUpdate> items,
        Guid? userId,
        CancellationToken ct = default);
    void Add(ShopMenu menu);
}
