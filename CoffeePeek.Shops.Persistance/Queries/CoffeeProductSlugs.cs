using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

/// <summary>Current public slugs of published catalog products; entries keep only the stable product ID.</summary>
internal static class CoffeeProductSlugs
{
    public static async Task<Dictionary<Guid, string>> ReadAsync(ShopsDbContext db, IEnumerable<Guid?> productIds,
        CancellationToken ct)
    {
        var ids = productIds.OfType<Guid>().Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await db.CoffeeProducts.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Status == CoffeePublicationStatus.Published && p.PublicSlug != null)
            .ToDictionaryAsync(p => p.Id, p => p.PublicSlug!, ct);
    }
}
