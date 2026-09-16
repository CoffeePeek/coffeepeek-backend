using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface ICoffeeShopRepository
{
    Task<CoffeeShop?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CoffeeShop?> GetByIdForOwnerAsync(Guid id, Guid ownerUserId, CancellationToken ct = default);
    Task<CoffeeShop?> GetByIdWithCatalogsAsync(Guid id, CancellationToken ct = default);
    Task<CoffeeShop?> GetByIdWithCatalogsForOwnerAsync(Guid id, Guid ownerUserId, CancellationToken ct = default);

    /// <summary>
    /// Inserts gallery photos without tracking the shop aggregate (owned schedules +
    /// sibling collections). Loading that graph made SaveChanges issue a 0-row UPDATE.
    /// </summary>
    Task<bool> TryAttachGalleryPhotosAsync(
        Guid shopId,
        Guid? ownerUserId,
        IReadOnlyList<ShopPhoto> photos,
        CancellationToken ct = default);
}