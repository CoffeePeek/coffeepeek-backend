using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Shelf;

/// <summary>Read-only CoffeeCatalog projection for Shelf; no tracking, no catalog writes.</summary>
public sealed class ShelfCatalogLookup(ShopsDbContext db) : IShelfCatalogLookup
{
    public async Task<ShelfCatalogProduct?> FindPublishedBySlugAsync(string slug, CancellationToken ct)
    {
        var product = await db.CoffeeProducts.AsNoTracking()
            .Where(p => p.PublicSlug == slug && p.Status == CoffeePublicationStatus.Published)
            .Select(p => new { p.Id, p.Name, RoasterName = db.Roasters.Where(r => r.Id == p.RoasterId).Select(r => r.Name).FirstOrDefault() })
            .SingleOrDefaultAsync(ct);
        return product is null ? null : new ShelfCatalogProduct(product.Id, product.Name, product.RoasterName);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetPublishedSlugsAsync(IReadOnlyCollection<Guid> productIds,
        CancellationToken ct) =>
        await CoffeeProductSlugs.ReadAsync(db, productIds.Select(id => (Guid?)id), ct);
}
