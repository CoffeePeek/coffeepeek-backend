using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

public sealed partial class CoffeeCatalogStore
{
    public Task<Dictionary<Guid, (int CoffeeShopsCount, int CoffeeProductsCount)>> RoasterCountsAsync(Guid[] ids, CancellationToken ct) =>
        db.Roasters.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new
        {
            r.Id,
            CoffeeShopsCount = r.CoffeeShops.Count(s => !s.IsHidden),
            CoffeeProductsCount = db.CoffeeProducts.Count(p => p.RoasterId == r.Id && p.Status == CoffeePublicationStatus.Published)
        }).ToDictionaryAsync(r => r.Id, r => (r.CoffeeShopsCount, r.CoffeeProductsCount), ct);
}
