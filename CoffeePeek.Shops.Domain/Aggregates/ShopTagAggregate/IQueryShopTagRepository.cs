namespace CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

public interface IQueryShopTagRepository
{
    Task<ShopTag[]> GetAllAsync(CancellationToken ct = default);
    Task<ShopTag[]> GetActiveAsync(CancellationToken ct = default);
    Task<ShopTag[]> GetActiveBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct = default);
    Task<bool> AllExistAndActiveAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken ct = default);
}