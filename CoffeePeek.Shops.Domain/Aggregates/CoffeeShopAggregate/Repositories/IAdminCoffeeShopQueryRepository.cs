namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IAdminCoffeeShopQueryRepository
{
    Task<(IReadOnlyList<CoffeeShop> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        CoffeeShopStatus? status,
        CancellationToken ct = default,
        bool? importedFromFile = null);

    Task<IReadOnlyList<CoffeeShop>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken ct = default);
}