using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.ShopsService.Discovery;

/// <summary>Cross-field rules shared by search endpoints; catalog-code checks live in the query store.</summary>
public static class DiscoveryRequestValidation
{
    public static Dictionary<string, string[]> Validate(object request)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string path, string message) => errors[path] = [message];
        void Query(string? q, string? sort)
        {
            if (q is null || q.Length > 100) Error("q", "Query must contain at most 100 characters.");
            if (sort == "relevance" && string.IsNullOrWhiteSpace(q)) Error("sort", "Relevance sorting requires a nonempty query.");
        }
        void Price(string path, string? currency, decimal? min, decimal? max)
        {
            if ((min.HasValue || max.HasValue) && currency is null) Error(path + ".currency", "Price filters require a currency.");
            if (min < 0 || max < 0 || min > max) Error(path, "Prices must be nonnegative and minimum must not exceed maximum.");
        }
        void Coffee(CoffeeFilters? f, string path, string? sort = null)
        {
            if (f is null) f = new();
            Price(path, f.Currency, f.MinPrice, f.MaxPrice);
            if (sort is "price_asc" or "price_desc" && (f.Currency is null || f.WeightGrams is null))
                Error("sort", "Price sorting requires currency and weightGrams.");
        }
        switch (request)
        {
            case CoffeeSearchRequest r:
                Query(r.Q, r.Sort); Coffee(r.Filters, "filters", r.Sort); break;
            case CoffeeFacetsRequest r:
                Query(r.Q, null); Coffee(r.Filters, "filters"); break;
            case CoffeeShopSearchRequest r:
                Query(r.Q, r.Sort);
                if (r.Filters?.RadiusKm.HasValue == true && r.Filters.Origin is null)
                    Error("filters.origin", "Radius requires an origin.");
                if (r.Filters?.RadiusKm <= 0) Error("filters.radiusKm", "Radius must be positive.");
                if (r.Sort == "distance_asc" && r.Filters?.Origin is null)
                    Error("sort", "Distance sorting requires an origin.");
                if (r.Filters?.Menu is { } menu) Price("filters.menu", menu.Currency, menu.MinPrice, menu.MaxPrice);
                break;
        }
        return errors;
    }
}
