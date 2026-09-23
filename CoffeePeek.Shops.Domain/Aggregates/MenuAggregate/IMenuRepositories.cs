namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed record ManualShopMenuItemUpdate(
    Guid DrinkDefinitionId,
    MenuItemAvailability Availability,
    decimal? Price,
    int? VolumeMl);

public interface IQueryCoffeeDrinkRepository
{
    Task<CoffeeDrinkDefinition[]> GetActiveAsync(CancellationToken ct = default);
}

public interface IQueryShopMenuRepository
{
    Task<ShopMenu?> GetByShopIdAsync(Guid shopId, CancellationToken ct = default);
}

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
