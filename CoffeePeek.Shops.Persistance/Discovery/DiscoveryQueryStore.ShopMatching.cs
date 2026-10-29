using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore;
using ShopPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore
{
    // Match confirmed method names, rather than assuming every pressure method makes espresso.
    // Menu drink categories remain the primary source for price/preparation matching.
    private static readonly string[] EspressoMethods = ["espresso", "эспрессо", "espresso machine", "эспрессо-машина"];
    private static readonly string[] FilterMethods = ["filter", "фильтр", "v60", "hario", "chemex", "кемекс", "aeropress", "аэропресс", "batch brew", "batch-brew", "drip", "воронка", "kalita", "калита"];

    private IQueryable<ShopMenuItem> MatchingMenuItems(MenuFilters f)
    {
        var items = db.Set<ShopMenuItem>().AsNoTracking().Where(i => i.Availability == MenuItemAvailability.Present &&
            i.DrinkDefinition != null && i.DrinkDefinition.IsActive);
        if (f.Brew.Length > 0)
        {
            var espresso = f.Brew.Contains("espresso"); var filter = f.Brew.Contains("filter");
            items = items.Where(i => espresso && i.DrinkDefinition!.Category == CoffeeDrinkCategory.Espresso || filter && i.DrinkDefinition!.Category == CoffeeDrinkCategory.Filter);
        }
        if (f.Currency is { } currency) items = items.Where(i => db.ShopMenus.Any(m => m.Id == i.ShopMenuId && m.Currency == currency));
        if (f.MinPrice is { } min) items = items.Where(i => i.Price.HasValue && i.Price >= min);
        if (f.MaxPrice is { } max) items = items.Where(i => i.Price.HasValue && i.Price <= max);
        return items;
    }

    private sealed class ShopHit
    {
        public CoffeeShop Shop { get; set; } = null!;
        public double? Distance { get; set; }
        public decimal Rating { get; set; }
        public int RatingCount { get; set; }
    }

    private sealed record ShopMatch(IQueryable<ShopHit> Hits, int Count, bool Fuzzy, string Query, DateTime Now);

    // Both presentations apply the same predicates and fuzzy fallback before pagination or map bounds.
    private async Task<ShopMatch> MatchShopsAsync(string query, ShopFilters f, Guid? userId, CancellationToken ct,
        bool includeTotal = true)
    {
        if ((f.VisitedOnly || f.FavoritesOnly) && !userId.HasValue)
            throw new DiscoveryException("Authentication is required for personal filters.", 401, "AUTH_REQUIRED");
        var roasters = await ResolveFiltersAsync(PublicAddressKind.Roaster, f.Roasters, "filters.roasters", ct);
        var equipment = await ResolveFiltersAsync(PublicAddressKind.Equipment, f.Equipments, "filters.equipments", ct);
        var beans = await ResolveFiltersAsync(PublicAddressKind.CoffeeBean, f.Beans, "filters.beans", ct);
        var methods = await ResolveFiltersAsync(PublicAddressKind.BrewMethod, f.BrewMethods, "filters.brewMethods", ct);
        var tags = await ResolveFiltersAsync(PublicAddressKind.ShopTag, f.Tags, "filters.tags", ct);
        var q = NormalizeQuery(query);
        var shops = db.Shops.AsNoTracking().Where(s => s.Status == CoffeeShopStatus.Active && !s.IsHidden && s.PublicSlug != null);
        if (f.City is not null)
        {
            var cityIds = await ResolveFiltersAsync(PublicAddressKind.City, [f.City], "filters.city", ct);
            shops = shops.Where(s => cityIds.Contains(s.Location.CityId));
        }
        if (roasters.Length > 0) shops = shops.Where(s => s.Roasters.Any(r => roasters.Contains(r.Id)));
        if (equipment.Length > 0) shops = shops.Where(s => s.ShopEquipments.Any(e => equipment.Contains(e.EquipmentId)));
        if (beans.Length > 0) shops = shops.Where(s => s.CoffeeBeans.Any(b => beans.Contains(b.Id)));
        if (methods.Length > 0) shops = shops.Where(s => s.BrewMethods.Any(b => methods.Contains(b.Id)));
        foreach (var tag in tags) shops = shops.Where(s => s.ShopTags.Any(t => t.TagId == tag));
        if (f.PriceRange is not null)
        {
            var price = f.PriceRange switch { "cheap" => ShopPriceRange.Cheap, "moderate" => ShopPriceRange.Moderate, _ => ShopPriceRange.Expensive };
            shops = shops.Where(s => s.PriceRange == price);
        }
        if (f.Type is not null)
        {
            var focus = f.Type switch { "specialty" => CoffeeFocusType.Specialty, "coffee-bar" => CoffeeFocusType.CoffeeBar, _ => CoffeeFocusType.Cafe };
            shops = shops.Where(s => s.Type == focus);
        }
        var now = DateTime.UtcNow;
        if (f.IsNew.HasValue)
        {
            var cutoff = now.AddDays(-BusinessConstants.ItNewEntityInDays);
            shops = f.IsNew.Value ? shops.Where(s => s.CreatedAtUtc >= cutoff) : shops.Where(s => s.CreatedAtUtc < cutoff);
        }
        if (f.VisitedOnly) shops = shops.Where(s => db.CheckIns.Any(c => c.ShopId == s.Id && c.UserId == userId && c.DeletedAtUtc == null));
        if (f.FavoritesOnly) shops = shops.Where(s => db.DiscoveryFavorites.Any(c => c.Kind == "coffee_shop" && c.EntityId == s.Id && c.UserId == userId));
        if (f.IsOpen.HasValue)
        {
            var day = now.DayOfWeek;
            var previous = day == DayOfWeek.Sunday ? DayOfWeek.Saturday : day - 1;
            var time = now.TimeOfDay;
            var open = shops.Where(s => s.Schedules.Any() && (
                s.Schedules.Any(d => d.DayOfWeek == day && !d.IsClosed && d.Intervals.Any(i =>
                    i.CloseTime >= i.OpenTime && time >= i.OpenTime && time <= i.CloseTime ||
                    i.CloseTime < i.OpenTime && time >= i.OpenTime)) ||
                s.Schedules.Any(d => d.DayOfWeek == previous && !d.IsClosed && d.Intervals.Any(i => i.CloseTime < i.OpenTime && time <= i.CloseTime))));
            shops = f.IsOpen.Value ? open : shops.Where(s => s.Schedules.Any() && !open.Any(o => o.Id == s.Id));
        }
        if (f.Menu is not null)
        {
            var menu = f.Menu;
            var items = MatchingMenuItems(menu);
            var positionRequired = menu.MinPrice.HasValue || menu.MaxPrice.HasValue;
            if (positionRequired || menu.Brew.Length > 0 || menu.Currency is not null)
            {
                var espresso = menu.Brew.Contains("espresso"); var filter = menu.Brew.Contains("filter");
                shops = shops.Where(s => db.ShopMenus.Any(m => m.CoffeeShopId == s.Id && items.Any(i => i.ShopMenuId == m.Id)) ||
                    !positionRequired && menu.Brew.Length > 0 && s.BrewMethods.Any(b =>
                        espresso && EspressoMethods.Contains(b.Name.ToLower()) || filter && FilterMethods.Contains(b.Name.ToLower())));
            }
        }
        var latitude = (double)(f.Origin?.Latitude ?? 0); var longitude = (double)(f.Origin?.Longitude ?? 0);
        var hasOrigin = f.Origin is not null;
        var ratings = CheckInEligibility.LatestRatings(db)
            .Select(r => new { r.ShopId, Value = (r.Rating!.Place + r.Rating.Coffee + r.Rating.Service) / 3m })
            .GroupBy(r => r.ShopId).Select(g => new
        {
            ShopId = g.Key,
            Rating = (decimal?)g.Average(r => r.Value),
            Count = (int?)g.Count()
        });
        var hits = from s in shops
            join ratingSummary in ratings on (Guid?)s.Id equals ratingSummary.ShopId into shopRatings
            from ratingSummary in shopRatings.DefaultIfEmpty()
            select new ShopHit
        {
            Shop = s,
            Rating = ratingSummary.Rating ?? 0,
            RatingCount = ratingSummary.Count ?? 0,
            Distance = hasOrigin && s.Location.Latitude.HasValue && s.Location.Longitude.HasValue ?
                12742000 * Math.Asin(Math.Sqrt(Math.Min(1.0,
                    Math.Pow(Math.Sin(((double)s.Location.Latitude.Value - latitude) * Math.PI / 360), 2) +
                    Math.Cos(latitude * Math.PI / 180) * Math.Cos((double)s.Location.Latitude.Value * Math.PI / 180) *
                    Math.Pow(Math.Sin(((double)s.Location.Longitude.Value - longitude) * Math.PI / 360), 2)))) : null
        };
        // Map geometry needs no rating aggregation or distance projection without these predicates.
        if (!includeTotal && f.MinRating is null && f.RadiusKm is null)
            hits = shops.Select(s => new ShopHit { Shop = s });
        if (f.MinRating is { } rating) hits = hits.Where(h => h.Rating >= rating);
        if (f.RadiusKm is { } radius) hits = hits.Where(h => h.Distance.HasValue && h.Distance <= (double)radius * 1000);
        var eligibleHits = hits;
        if (q.Length > 0) hits = hits.Where(h => h.Shop.Name.ToLower().Replace("ё", "е").Contains(q) ||
            h.Shop.Location.Address.ToLower().Replace("ё", "е").Contains(q));
        // Only the list needs a total. Map requests need existence solely to decide the same fuzzy fallback.
        var count = includeTotal ? await hits.CountAsync(ct)
            : q.Length > 0 && await hits.AnyAsync(ct) ? 1 : 0;
        var fuzzy = count == 0 && q.Count(char.IsLetter) >= 4;
        if (fuzzy)
        {
            // Keep indexable pg_trgm operators: a similarity() threshold alone cannot use the GIN index.
            var candidates = await eligibleHits.Where(h =>
                    EF.Functions.TrigramsAreSimilar(h.Shop.Name.ToLower().Replace("ё", "е"), q) ||
                    EF.Functions.TrigramsAreWordSimilar(q, h.Shop.Name.ToLower().Replace("ё", "е")))
                .OrderByDescending(h => EF.Functions.TrigramsWordSimilarity(q, h.Shop.Name.ToLower().Replace("ё", "е")))
                .ThenBy(h => h.Shop.Name).ThenBy(h => h.Shop.PublicSlug)
                // shortcut: fuzzy totals cover the best 200 eligible names; revisit with measured recall and latency.
                .Take(200).Select(h => h.Shop.Id).ToArrayAsync(ct);
            count = candidates.Length;
            hits = eligibleHits.Where(h => candidates.Contains(h.Shop.Id));
        }
        return new(hits, count, fuzzy, q, now);
    }
}
