using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore
{
    public async Task<CoffeeShopPage> SearchShopsAsync(CoffeeShopSearchRequest request, Guid? userId, CancellationToken ct)
    {
        var f = request.Filters ?? new();
        var match = await MatchShopsAsync(request.Q, f, userId, ct);
        var (hits, count, fuzzy, q, now) = match;
        if (count == 0) return new() { CurrentPage = request.Page, PageSize = request.PageSize };
        var sort = request.Sort ?? (q.Length > 0 ? "relevance" : "recommended");
        IOrderedQueryable<ShopHit> ordered = sort switch
        {
            "recommended" => RecommendShops(hits, userId),
            "rating_desc" => hits.OrderByDescending(h => h.Rating),
            "distance_asc" => hits.OrderBy(h => h.Distance == null).ThenBy(h => h.Distance),
            "relevance" when fuzzy => hits.OrderByDescending(h =>
                EF.Functions.TrigramsWordSimilarity(q, h.Shop.Name.ToLower().Replace("ё", "е"))),
            "relevance" => hits.OrderBy(h => h.Shop.Name.ToLower().Replace("ё", "е") == q ? 0 :
                h.Shop.Name.ToLower().Replace("ё", "е").StartsWith(q) ? 1 :
                h.Shop.Name.ToLower().Replace("ё", "е").Contains(" " + q) ? 2 :
                h.Shop.Name.ToLower().Replace("ё", "е").Contains(q) ? 3 : 4),
            _ => hits.OrderBy(h => h.Shop.Name)
        };
        var page = await ordered.ThenBy(h => h.Shop.Name).ThenBy(h => h.Shop.PublicSlug)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(h => new { h.Shop.Id, h.Rating, h.Distance }).ToArrayAsync(ct);
        var ids = page.Select(h => h.Id).ToArray();
        var rows = await db.Shops.AsNoTracking().Where(s => ids.Contains(s.Id)).Include(s => s.Schedules).ThenInclude(s => s.Intervals)
            .Include(s => s.ShopPhotos).Include(s => s.ShopTags).ThenInclude(t => t.Tag).AsSplitQuery().ToDictionaryAsync(s => s.Id, ct);
        var cityIdsOnPage = rows.Values.Select(s => s.Location.CityId).Distinct().ToArray();
        var cities = await db.Cities.AsNoTracking().Where(c => cityIdsOnPage.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var visited = userId.HasValue ? await db.CheckIns.AsNoTracking().Where(c => c.UserId == userId && c.ShopId != null && c.DeletedAtUtc == null && ids.Contains(c.ShopId.Value)).Select(c => c.ShopId!.Value).Distinct().ToArrayAsync(ct) : [];
        var favorites = userId.HasValue ? await db.DiscoveryFavorites.AsNoTracking().Where(c => c.UserId == userId && c.Kind == "coffee_shop" && ids.Contains(c.EntityId)).Select(c => c.EntityId).ToArrayAsync(ct) : [];
        var checkInCounts = await CheckInEligibility.Public(db).Where(r => r.ShopId != null && ids.Contains(r.ShopId.Value))
            .GroupBy(r => r.ShopId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var matchedMenu = f.Menu is null ? null : MatchingMenuItems(f.Menu);
        var positions = matchedMenu is null ? [] : await (
            from m in db.ShopMenus.AsNoTracking() where ids.Contains(m.CoffeeShopId)
            join i in matchedMenu on m.Id equals i.ShopMenuId
            select new { m.CoffeeShopId, m.Currency, m.CapturedAtUtc, i.Price, i.DrinkDefinition!.Slug, i.DrinkDefinition.NameRu, i.DrinkDefinition.Category }
        ).ToArrayAsync(ct);
        return new()
        {
            Items = page.Select(h =>
            {
                var s = rows[h.Id]; cities.TryGetValue(s.Location.CityId, out var city);
                return new CoffeeShopCard
                {
                    Address = Address(s.PublicSlug!, "coffee-shops", s.SlugRevision), Name = s.Name,
                    City = city?.PublicSlug is null ? null : Address(city.PublicSlug, "cities", city.SlugRevision),
                    AddressLine = s.Location.Address, CoverPhoto = Photo(s.ShopPhotos.OrderBy(p => p.SortIndex).FirstOrDefault()?.StorageKey),
                    Rating = Math.Round(h.Rating, 2), CheckInCount = checkInCounts.GetValueOrDefault(s.Id),
                    IsOpen = s.IsOpenAtUtc(now),
                    IsVisited = userId.HasValue ? visited.Contains(s.Id) : null,
                    IsFavorite = userId.HasValue ? favorites.Contains(s.Id) : null,
                    DistanceMeters = h.Distance.HasValue ? (int)Math.Round(h.Distance.Value) : null,
                    Tags = s.ShopTags.Where(t => t.Tag?.IsActive == true).OrderBy(t => t.Tag!.SortOrder).ThenBy(t => t.Tag!.Slug)
                        .Select(t => new PublicTag { Slug = t.Tag!.PublicSlug ?? t.Tag.Slug, Name = t.Tag.Name, Description = t.Tag.Description, SortOrder = t.Tag.SortOrder }).ToArray(),
                    MatchingMenuItems = positions.Where(p => p.CoffeeShopId == s.Id).OrderBy(p => p.Slug).Select(p => new MatchedMenuItem
                    {
                        DrinkSlug = p.Slug, Name = p.NameRu, BrewMethod = p.Category == CoffeeDrinkCategory.Espresso ? "espresso" : "filter",
                        Price = p.Price, Currency = p.Currency, CheckedAtUtc = p.CapturedAtUtc
                    }).ToArray()
                };
            }).ToArray(), TotalItems = count, TotalPages = (count + request.PageSize - 1) / request.PageSize, CurrentPage = request.Page, PageSize = request.PageSize
        };
    }

    private IOrderedQueryable<ShopHit> RecommendShops(IQueryable<ShopHit> hits, Guid? userId)
    {
        var favorites = db.DiscoveryFavorites.Where(f => f.UserId == userId);
        var favoriteShops = favorites.Where(f => f.Kind == "coffee_shop").Select(f => f.EntityId);
        var favoriteRoasters = favorites.Where(f => f.Kind == "roaster").Select(f => f.EntityId);
        var favoriteDrinks = favorites.Where(f => f.Kind == "drink").Select(f => f.EntityId);
        var liked = CheckInEligibility.LatestRatings(db).Where(r => r.UserId == userId &&
            (r.Rating!.Place + r.Rating.Coffee + r.Rating.Service) / 3m >= 4m).Select(r => r.ShopId!.Value);
        var roasterShops = db.Shops.Where(s => s.Roasters.Any(r => favoriteRoasters.Contains(r.Id))).Select(s => s.Id);
        var drinks = db.Set<ShopMenuItem>().Where(i => i.Availability == MenuItemAvailability.Present &&
            i.DrinkDefinition != null && i.DrinkDefinition.IsActive &&
            favoriteDrinks.Contains(i.DrinkDefinitionId));
        var drinkShops = from menu in db.ShopMenus join drink in drinks on menu.Id equals drink.ShopMenuId select menu.CoffeeShopId;

        // shortcut: fixed prior and weights are a cold-start baseline; recalibrate with ranking outcome data.
        return hits.OrderByDescending(h =>
            0.45 * ((double)h.Rating * h.RatingCount + 3.5 * 5) / ((h.RatingCount + 5) * 5)
            + (h.Distance.HasValue ? 0.35 / (1 + h.Distance.Value / 1500) : 0)
            + 0.05 * h.Shop.DataCompletenessScore / 100
            + (userId.HasValue ? 0.15 * (
                favoriteShops.Contains(h.Shop.Id) || liked.Contains(h.Shop.Id)
                    ? 1.0
                    : (roasterShops.Contains(h.Shop.Id) ? 0.6 : 0)
                    + (drinkShops.Contains(h.Shop.Id) ? 0.4 : 0)) : 0));
    }
}
