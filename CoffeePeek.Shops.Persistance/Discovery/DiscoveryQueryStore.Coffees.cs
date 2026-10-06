using CoffeePeek.Contract.Dtos.Discovery;
using System.Linq.Expressions;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore
{
    private static bool RequiresOffer(CoffeeFilters f) => f.AvailableOnly || f.Brew.Length > 0 || f.WeightGrams.HasValue ||
        f.Currency is not null || f.MinPrice.HasValue || f.MaxPrice.HasValue || f.AvailabilityScope is not null;

    private IQueryable<CoffeeVariant> MatchingVariants(CoffeeFilters f, CatalogState catalog)
    {
        var q = db.Set<CoffeeVariant>().AsNoTracking();
        if (f.AvailableOnly) q = q.Where(v => v.Availability == CoffeeAvailability.InStock);
        if (f.WeightGrams is { } weight) q = q.Where(v => v.WeightGrams == weight);
        if (f.Currency is { } currency) q = q.Where(v => v.Currency == currency);
        if (f.MinPrice is { } min) q = q.Where(v => v.Price >= min);
        if (f.MaxPrice is { } max) q = q.Where(v => v.Price <= max);
        if (f.AvailabilityScope is { } scope) q = q.Where(v => v.AvailabilityScope == scope);
        if (f.Brew.Length > 0)
        {
            var purposes = f.Brew.ToList();
            if ((f.Brew.Contains("espresso") || f.Brew.Contains("filter")) && catalog.Active["brew"].Contains("omni")) purposes.Add("omni");
            var selected = purposes.Distinct().ToArray();
            q = WhereAnyCode(q, selected, (v, code) => v.BrewPurposeCode == code ||
                v.BrewPurposeCode == null && v.Source.Product.DefaultBrewPurposes.Contains(code));
        }
        return q;
    }

    private IQueryable<CoffeeProduct> MatchingProducts(CoffeeFilters f, CatalogState c, Guid[] roasterIds, string q = "")
    {
        var products = db.CoffeeProducts.AsNoTracking().Where(p => p.Status == CoffeePublicationStatus.Published && p.PublicSlug != null &&
            db.Roasters.Any(r => r.Id == p.RoasterId && r.PublicSlug != null));
        if (q.Length > 0) products = products.Where(p => p.Name.ToLower().Replace("ё", "е").Contains(q) ||
            db.Roasters.Any(r => r.Id == p.RoasterId && r.Name.ToLower().Replace("ё", "е").Contains(q)));
        if (roasterIds.Length > 0) products = products.Where(p => roasterIds.Contains(p.RoasterId));
        if (f.Countries.Length > 0) products = products.Where(p => p.Countries.Any(c => f.Countries.Contains(c.CountryCode)));
        if (f.ProductKind is { } kind) products = products.Where(p => p.ProductKind == kind);
        if (f.ProductForm is { } form) products = products.Where(p => p.ProductForm == form);
        if (f.Caffeine.Length > 0) products = products.Where(p => p.CaffeineCode != null && f.Caffeine.Contains(p.CaffeineCode));
        if (f.Roast.Length > 0) products = products.Where(p => p.RoastLevelCode != null && f.Roast.Contains(p.RoastLevelCode));
        if (f.Acidity.Length > 0) products = products.Where(p => p.AcidityCode != null && f.Acidity.Contains(p.AcidityCode));
        if (f.Composition.Length > 0) products = products.Where(p => p.CompositionCode != null && f.Composition.Contains(p.CompositionCode));
        if (f.Processing.Length > 0) products = WhereAnyCode(products, f.Processing, (p, code) => p.ProcessingCodes.Contains(code));
        if (f.Fermentation.Length > 0) products = WhereAnyCode(products, f.Fermentation, (p, code) => p.FermentationCodes.Contains(code));
        if (f.Taste.Length > 0) products = WhereAnyCode(products, f.Taste, (p, code) => p.TasteGroupCodes.Contains(code));
        if (RequiresOffer(f))
        {
            var variants = MatchingVariants(f, c);
            products = products.Where(p => variants.Any(v => v.Source.CoffeeProductId == p.Id));
        }
        return products;
    }

    public async Task<CoffeePage> SearchCoffeesAsync(CoffeeSearchRequest request, CancellationToken ct)
    {
        var c = await CatalogAsync(ct);
        var f = request.Filters ?? new();
        var ids = await ValidateCoffeeAsync(f, c, "filters", ct);
        var q = NormalizeQuery(request.Q);
        var products = MatchingProducts(f, c, ids, q);
        var count = await products.CountAsync(ct);
        var sort = request.Sort ?? (q.Length > 0 ? "relevance" : "name_asc");
        var variants = MatchingVariants(f, c);
        IOrderedQueryable<CoffeeProduct> ordered = sort switch
        {
            "price_asc" => products.OrderBy(p => variants.Where(v => v.Source.CoffeeProductId == p.Id).Min(v => (decimal?)v.Price)),
            "price_desc" => products.OrderByDescending(p => variants.Where(v => v.Source.CoffeeProductId == p.Id).Min(v => (decimal?)v.Price)),
            "newest" => products.OrderByDescending(p => p.CreatedAtUtc),
            "relevance" => products.OrderBy(p =>
                p.Name.ToLower().Replace("ё", "е") == q ? 0 :
                p.Name.ToLower().Replace("ё", "е").StartsWith(q) ? 1 :
                p.Name.ToLower().Replace("ё", "е").Contains(" " + q) ? 2 :
                p.Name.ToLower().Replace("ё", "е").Contains(q) ? 3 :
                db.Roasters.Any(r => r.Id == p.RoasterId && r.Name.ToLower().Replace("ё", "е") == q) ? 4 :
                db.Roasters.Any(r => r.Id == p.RoasterId && r.Name.ToLower().Replace("ё", "е").StartsWith(q)) ? 5 : 6),
            _ => products.OrderBy(p => p.Name)
        };
        var rows = await CardDetails(ordered.ThenBy(p => p.Name).ThenBy(p => p.PublicSlug)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)).ToArrayAsync(ct);
        return new()
        {
            Items = await CardsAsync(rows, f, c, sort is "price_asc" or "price_desc", ct),
            TotalItems = count, TotalPages = (count + request.PageSize - 1) / request.PageSize,
            CurrentPage = request.Page, PageSize = request.PageSize
        };
    }

    private async Task<CoffeeCard[]> CardsAsync(CoffeeProduct[] rows, CoffeeFilters filters, CatalogState c, bool sortPrice, CancellationToken ct)
    {
        var ids = rows.Select(p => p.Id).ToArray();
        var roasterIds = rows.Select(p => p.RoasterId).Distinct().ToArray();
        var roasters = await db.Roasters.AsNoTracking().Include(r => r.Photos).Where(r => roasterIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        // Only the page's matching variants are materialized. This preserves one-variant matching and
        // avoids leaking cheap/out-of-stock/differently sized offers into a filtered result.
        var variants = await MatchingVariants(filters, c).Where(v => ids.Contains(v.Source.CoffeeProductId))
            .Include(v => v.Source).OrderBy(v => v.WeightGrams).ThenBy(v => v.Id).ToArrayAsync(ct);
        var byProduct = variants.ToLookup(v => v.Source.CoffeeProductId);
        return rows.Select(p => new CoffeeCard
        {
            Address = Address(p.PublicSlug!, "coffees", p.SlugRevision), Name = p.Name,
            Roaster = Identity(roasters[p.RoasterId]), ProductKind = p.ProductKind, ProductForm = p.ProductForm,
            Classification = Classification(p, c), Countries = Countries(p), CoverPhoto = Photos(p).FirstOrDefault(),
            MatchingOffers = byProduct[p.Id].Select(v => Offer(v, c)).ToArray(),
            SortPrice = sortPrice ? byProduct[p.Id].Select(v => (decimal?)v.Price).DefaultIfEmpty().Min() : null,
            CreatedAtUtc = p.CreatedAtUtc, CatalogCheckedAtUtc = p.Sources.Select(s => (DateTime?)s.LastSeenAtUtc).DefaultIfEmpty().Max()
        }).ToArray();
    }

    private static string NormalizeQuery(string? q) => string.Join(" ", (q ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant().Replace('ё', 'е');

    // EF inlines Invoke before translation. Constant Contains predicates work in both PostgreSQL and InMemory.
    private static IQueryable<T> WhereAnyCode<T>(IQueryable<T> query, string[] codes, Expression<Func<T, string, bool>> condition)
    {
        var parameter = Expression.Parameter(typeof(T), "entity");
        var predicates = codes.Distinct().Select(code => (Expression)Expression.Invoke(condition, parameter, Expression.Constant(code)));
        var body = predicates.Aggregate(Expression.OrElse);
        return query.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
    }

}
