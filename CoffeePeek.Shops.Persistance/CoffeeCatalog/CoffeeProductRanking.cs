using System.Linq.Expressions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

internal static class CoffeeProductRanking
{
    // Keep names beginning with a letter ahead of numbered or decorated names.
    // PostgreSQL's Unicode regex classes depend on the database locale.
    internal const string NonLetterPrefix = @"^\W|^_|^\d";

    // Count distinct pieces of useful card information, rather than the number of offers or photos.
    internal static readonly Expression<Func<CoffeeProduct, int>> Completeness = p =>
        (p.Sources.Any(s => s.Photos.Any()) ? 1 : 0) +
        (!string.IsNullOrEmpty(p.Description) ? 1 : 0) +
        (p.Countries.Any() ? 1 : 0) +
        (p.Sources.Any(s => s.Variants.Any()) ? 1 : 0) +
        (!string.IsNullOrEmpty(p.Processing) ? 1 : 0) +
        (!string.IsNullOrEmpty(p.RoastLevel) ? 1 : 0) +
        (!string.IsNullOrEmpty(p.Acidity) ? 1 : 0) +
        (!string.IsNullOrEmpty(p.Body) ? 1 : 0);
}
