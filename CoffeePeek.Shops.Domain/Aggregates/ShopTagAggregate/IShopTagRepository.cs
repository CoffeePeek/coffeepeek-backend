namespace CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

public interface IShopTagRepository
{
    Task<ShopTag?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ShopTag?> GetBySlugAsync(string slug, CancellationToken ct = default);
    void Add(ShopTag tag);
}