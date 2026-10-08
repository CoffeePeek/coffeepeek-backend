using CoffeePeek.Contract.Dtos.Discovery;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore
{
    private static CoffeeFilters CloneFilter(CoffeeFilters f) => new()
    {
        Roasters = f.Roasters, Countries = f.Countries, ProductKind = f.ProductKind, ProductForm = f.ProductForm,
        Brew = f.Brew, Caffeine = f.Caffeine, Roast = f.Roast, Acidity = f.Acidity,
        Processing = f.Processing, Fermentation = f.Fermentation, Taste = f.Taste, Composition = f.Composition,
        WeightGrams = f.WeightGrams, Currency = f.Currency, MinPrice = f.MinPrice, MaxPrice = f.MaxPrice,
        AvailabilityScope = f.AvailabilityScope, AvailableOnly = f.AvailableOnly
    };

    private static CoffeeFilters WithGroup(CoffeeFilters f, string group, string[] selection)
    {
        var copy = CloneFilter(f);
        switch (group)
        {
            case "brew": copy.Brew = selection; break;
            case "caffeine": copy.Caffeine = selection; break;
            case "roast": copy.Roast = selection; break;
            case "acidity": copy.Acidity = selection; break;
            case "processing": copy.Processing = selection; break;
            case "fermentation": copy.Fermentation = selection; break;
            case "taste": copy.Taste = selection; break;
            case "composition": copy.Composition = selection; break;
        }
        return copy;
    }

    public async Task<CoffeeFacetsResult> CoffeeFacetsAsync(CoffeeFacetsRequest request, CancellationToken ct)
    {
        var c = await CatalogAsync(ct);
        var f = request.Filters ?? new();
        var roasterIds = await ValidateCoffeeAsync(f, c, "filters", ct);
        var q = NormalizeQuery(request.Q);
        var result = new CoffeeFacetsResult { TotalItems = await MatchingProducts(f, c, roasterIds, q).CountAsync(ct) };
        var groups = new List<FacetGroup>();
        foreach (var group in Groups)
        {
            var options = new List<FacetOption>();
            foreach (var v in c.Values.Where(v => v.GroupCode == group))
            {
                var filter = WithGroup(f, group, [v.Code]);
                options.Add(new() { Code = v.Code, Name = v.Name, Selected = Selection(f, group).Contains(v.Code),
                    Count = await MatchingProducts(filter, c, roasterIds, q).CountAsync(ct) });
            }
            groups.Add(new() { Code = group, Name = GroupNames[group], Selection = "or", Options = options.ToArray() });
        }
        result.Groups = groups.ToArray();
        if (f.Currency is not null && f.WeightGrams.HasValue)
        {
            var withoutPrice = CloneFilter(f); withoutPrice.MinPrice = null; withoutPrice.MaxPrice = null;
            var products = MatchingProducts(withoutPrice, c, roasterIds, q);
            var variants = MatchingVariants(withoutPrice, c).Where(v => products.Any(p => p.Id == v.Source.CoffeeProductId));
            var range = await variants.GroupBy(v => v.Currency).Select(g => new { Min = g.Min(v => v.Price), Max = g.Max(v => v.Price) }).SingleOrDefaultAsync(ct);
            if (range is not null) result.PriceRange = new() { Currency = f.Currency, WeightGrams = f.WeightGrams.Value, Min = range.Min, Max = range.Max };
        }
        return result;
    }
}
