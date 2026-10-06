using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore
{
    private IQueryable<Roaster> MatchingRoasters(RoasterFilters f, CatalogState c, Guid? userId, string q)
    {
        var roasters = db.Roasters.AsNoTracking().Where(r => r.PublicSlug != null);
        if (q.Length > 0) roasters = roasters.Where(r => r.Name.ToLower().Replace("ё", "е").Contains(q));
        foreach (var tag in f.Tags.Distinct())
            roasters = roasters.Where(r => db.RoasterTagAssignments.Any(a => a.RoasterId == r.Id && a.Tag.IsActive && a.Tag.Slug == tag));
        if (f.ExcludeTags.Length > 0)
            roasters = roasters.Where(r => !db.RoasterTagAssignments.Any(a => a.RoasterId == r.Id && a.Tag.IsActive && f.ExcludeTags.Contains(a.Tag.Slug)));
        if (f.FavoritesOnly)
        {
            if (!userId.HasValue) throw new DiscoveryException("Authentication is required for favorites.", 401, "AUTH_REQUIRED");
            roasters = roasters.Where(r => db.DiscoveryFavorites.Any(a => a.UserId == userId && a.Kind == "roaster" && a.EntityId == r.Id));
        }
        if (f.Coffee is not null)
        {
            var products = MatchingProducts(f.Coffee, c, []);
            roasters = roasters.Where(r => products.Any(p => p.RoasterId == r.Id));
        }
        return roasters;
    }

    public async Task<RoasterPage> SearchRoastersAsync(RoasterSearchRequest request, Guid? userId, CancellationToken ct)
    {
        var c = await CatalogAsync(ct);
        var f = request.Filters ?? new();
        await ValidateTagsAsync(f, ct);
        if (f.Coffee is not null) await ValidateCoffeeAsync(f.Coffee, c, "filters.coffee", ct);
        var q = NormalizeQuery(request.Q);
        var roasters = MatchingRoasters(f, c, userId, q);
        var count = await roasters.CountAsync(ct);
        var coffeeFilters = f.Coffee ?? new CoffeeFilters();
        var products = MatchingProducts(coffeeFilters, c, []);
        var sort = request.Sort ?? (q.Length > 0 ? "relevance" : "name_asc");
        IOrderedQueryable<Roaster> ordered = sort switch
        {
            "available_coffees_desc" => roasters.OrderByDescending(r => products.Count(p => p.RoasterId == r.Id)),
            "relevance" => roasters.OrderBy(r => r.Name.ToLower().Replace("ё", "е") == q ? 0 :
                r.Name.ToLower().Replace("ё", "е").StartsWith(q) ? 1 : r.Name.ToLower().Replace("ё", "е").Contains(" " + q) ? 2 : 3),
            _ => roasters.OrderBy(r => r.Name)
        };
        var rows = await ordered.ThenBy(r => r.Name).ThenBy(r => r.PublicSlug).Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize).Include(r => r.Photos).AsSplitQuery().ToArrayAsync(ct);
        var ids = rows.Select(r => r.Id).ToArray();
        var tags = await db.RoasterTagAssignments.AsNoTracking().Where(a => ids.Contains(a.RoasterId) && a.Tag.IsActive)
            .OrderBy(a => a.Tag.SortOrder).ThenBy(a => a.Tag.Slug).Select(a => new { a.RoasterId, a.Tag.Slug, a.Tag.Name, a.Tag.Description, a.Tag.SortOrder }).ToArrayAsync(ct);
        var favorites = userId.HasValue ? await db.DiscoveryFavorites.AsNoTracking()
            .Where(a => a.UserId == userId && a.Kind == "roaster" && ids.Contains(a.EntityId)).Select(a => a.EntityId).ToArrayAsync(ct) : [];
        var available = MatchingProducts(new CoffeeFilters(), c, []);
        var stats = await db.Roasters.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new
        {
            r.Id,
            CoffeeShopsCount = r.CoffeeShops.Count(s => !s.IsHidden),
            CoffeeProductsCount = db.CoffeeProducts.Count(p => p.RoasterId == r.Id && p.Status == CoffeePublicationStatus.Published),
            Available = available.Count(p => p.RoasterId == r.Id),
            Matching = products.Count(p => p.RoasterId == r.Id),
            FirstProductId = products.Where(p => p.RoasterId == r.Id).OrderBy(p => p.Name).ThenBy(p => p.PublicSlug).Select(p => (Guid?)p.Id).FirstOrDefault(),
            Checked = db.CoffeeProductSources.Where(s => s.Product.RoasterId == r.Id && s.Product.Status == CoffeePublicationStatus.Published).Max(s => (DateTime?)s.LastSeenAtUtc)
        }).ToDictionaryAsync(x => x.Id, ct);
        var firstIds = f.Coffee is null ? [] : stats.Values.Where(s => s.FirstProductId.HasValue).Select(s => s.FirstProductId!.Value).ToArray();
        var firstProducts = await CardDetails(db.CoffeeProducts.AsNoTracking().Where(p => firstIds.Contains(p.Id))).ToArrayAsync(ct);
        var cards = (await CardsAsync(firstProducts, coffeeFilters, c, false, ct)).ToDictionary(p => p.Roaster.Address.Slug);
        return new()
        {
            Items = rows.Select(r => new RoasterCard
            {
                Address = Address(r.PublicSlug!, "roasters", r.SlugRevision), Name = r.Name,
                CoverPhoto = Photo(r.Photos.OrderBy(p => p.SortIndex).FirstOrDefault()?.StorageKey),
                Tags = tags.Where(t => t.RoasterId == r.Id).Select(t => new PublicTag { Slug = t.Slug, Name = t.Name, Description = t.Description, SortOrder = t.SortOrder }).ToArray(),
                IsFavorite = userId.HasValue ? favorites.Contains(r.Id) : null, AvailableCoffeeProducts = stats[r.Id].Available,
                CoffeeShopsCount = stats[r.Id].CoffeeShopsCount, CoffeeProductsCount = stats[r.Id].CoffeeProductsCount,
                MatchingCoffeeProducts = f.Coffee is null ? null : stats[r.Id].Matching,
                CoffeeCatalogUpdatedAtUtc = stats[r.Id].Checked,
                MatchingCoffee = f.Coffee is not null && cards.TryGetValue(r.PublicSlug!, out var card) ? card : null
            }).ToArray(), TotalItems = count, TotalPages = (count + request.PageSize - 1) / request.PageSize,
            CurrentPage = request.Page, PageSize = request.PageSize
        };
    }

    public async Task<DiscoverySearchResult> SearchDiscoveryAsync(DiscoverySearchRequest request, Guid? userId, CancellationToken ct)
    {
        var filters = request.Filters ?? new();
        var budget = filters.Budget;
        var coffeeCriteria = filters.Brew.Length > 0 || budget?.MinCoffeePrice.HasValue == true ||
            budget?.MaxCoffeePrice.HasValue == true || budget?.CoffeeWeightGrams.HasValue == true;
        var menuCriteria = filters.Brew.Length > 0 || budget?.MinDrinkPrice.HasValue == true ||
            budget?.MaxDrinkPrice.HasValue == true || budget?.DrinkVolumeMl.HasValue == true;
        var result = new DiscoverySearchResult { Q = NormalizeQuery(request.Q) };
        // Execute sequentially: both sections share one scoped DbContext. An exception is propagated
        // as a dependency error; a failed section must never be presented as an empty successful page.
        if (request.Sections.Contains("coffee_shops")) result.CoffeeShops = await SearchShopsAsync(new()
        {
            Q = request.Q, Sort = request.Sort, Page = request.CoffeeShops?.Page ?? 1, PageSize = request.CoffeeShops?.PageSize ?? 10,
            Filters = new() { Menu = menuCriteria ? new MenuFilters
            {
                Brew = filters.Brew, Currency = budget?.Currency, MinPrice = budget?.MinDrinkPrice,
                MaxPrice = budget?.MaxDrinkPrice, VolumeMl = budget?.DrinkVolumeMl
            } : null }
        }, userId, ct);
        if (request.Sections.Contains("roasters")) result.Roasters = await SearchRoastersAsync(new()
        {
            Q = request.Q, Sort = request.Sort, Page = request.Roasters?.Page ?? 1, PageSize = request.Roasters?.PageSize ?? 10,
            Filters = new() { Coffee = coffeeCriteria ? new RoasterCoffeeFilters
            {
                Brew = filters.Brew, Currency = budget?.Currency, MinPrice = budget?.MinCoffeePrice,
                MaxPrice = budget?.MaxCoffeePrice, WeightGrams = budget?.CoffeeWeightGrams, AvailableOnly = true
            } : null }
        }, userId, ct);
        return result;
    }
}
