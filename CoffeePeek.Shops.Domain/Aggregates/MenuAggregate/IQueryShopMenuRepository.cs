namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public interface IQueryShopMenuRepository
{
    Task<ShopMenu?> GetByShopIdAsync(Guid shopId, CancellationToken ct = default);
}
