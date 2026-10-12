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
        if (f.VolumeMl is { } volume) items = items.Where(i => i.VolumeMl == volume);
        return items;
    }

    private sealed class ShopHit
    {
        public CoffeeShop Shop { get; set; } = null!;
        public double? Distance { get; set; }
        public decimal Rating { get; set; }
    }

    public async Task<CoffeeShopPage> SearchShopsAsync(CoffeeShopSearchRequest request, Guid? userId, CancellationToken ct)
    {
        var f = request.Filters ?? new();
        if ((f.VisitedOnly || f.FavoritesOnly) && !userId.HasValue)
            throw new DiscoveryException("Authentication is required for personal filters.", 401, "AUTH_REQUIRED");
        var roasters = await ResolveFiltersAsync(PublicAddressKind.Roaster, f.Roasters, "filters.roasters", ct);
        var equipment = await ResolveFiltersAsync(PublicAddressKind.Equipment, f.Equipments, "filters.equipments", ct);
        var beans = await ResolveFiltersAsync(PublicAddressKind.CoffeeBean, f.Beans, "filters.beans", ct);
        var methods = await ResolveFiltersAsync(PublicAddressKind.BrewMethod, f.BrewMethods, "filters.brewMethods", ct);
        var tags = await ResolveFiltersAsync(PublicAddressKind.ShopTag, f.Tags, "filters.tags", ct);
        var q = NormalizeQuery(request.Q);
        var shops = db.Shops.AsNoTracking().Where(s => s.Status == CoffeeShopStatus.Active && !s.IsHidden && s.PublicSlug != null);
        if (q.Length > 0) shops = shops.Where(s => s.Name.ToLower().Replace("ё", "е").Contains(q));
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
            var positionRequired = menu.MinPrice.HasValue || menu.MaxPrice.HasValue || menu.VolumeMl.HasValue;
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
        var hits = shops.Select(s => new ShopHit
        {
            Shop = s,
            Rating = CheckInEligibility.LatestRatings(db).Where(r => r.ShopId == s.Id)
                .Select(r => (decimal?)((r.Rating!.Place + r.Rating.Coffee + r.Rating.Service) / 3m)).Average() ?? 0,
            Distance = hasOrigin && s.Location.Latitude.HasValue && s.Location.Longitude.HasValue ?
                12742000 * Math.Asin(Math.Sqrt(Math.Min(1.0,
                    Math.Pow(Math.Sin(((double)s.Location.Latitude.Value - latitude) * Math.PI / 360), 2) +
                    Math.Cos(latitude * Math.PI / 180) * Math.Cos((double)s.Location.Latitude.Value * Math.PI / 180) *
                    Math.Pow(Math.Sin(((double)s.Location.Longitude.Value - longitude) * Math.PI / 360), 2)))) : null
        });
        if (f.MinRating is { } rating) hits = hits.Where(h => h.Rating >= rating);
        if (f.RadiusKm is { } radius) hits = hits.Where(h => h.Distance.HasValue && h.Distance <= (double)radius * 1000);
        var count = await hits.CountAsync(ct);
        var sort = request.Sort ?? (q.Length > 0 ? "relevance" : hasOrigin ? "distance_asc" : "name_asc");
        IOrderedQueryable<ShopHit> ordered = sort switch
        {
            "rating_desc" => hits.OrderByDescending(h => h.Rating),
            "distance_asc" => hits.OrderBy(h => h.Distance == null).ThenBy(h => h.Distance),
            "relevance" => hits.OrderBy(h => h.Shop.Name.ToLower().Replace("ё", "е") == q ? 0 :
                h.Shop.Name.ToLower().Replace("ё", "е").StartsWith(q) ? 1 : h.Shop.Name.ToLower().Replace("ё", "е").Contains(" " + q) ? 2 : 3),
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
        var visited = userId.HasValue ? await db.CheckIns.AsNoTracking().Where(c => c.UserId == userId && c.DeletedAtUtc == null && ids.Contains(c.ShopId)).Select(c => c.ShopId).Distinct().ToArrayAsync(ct) : [];
        var favorites = userId.HasValue ? await db.DiscoveryFavorites.AsNoTracking().Where(c => c.UserId == userId && c.Kind == "coffee_shop" && ids.Contains(c.EntityId)).Select(c => c.EntityId).ToArrayAsync(ct) : [];
        var checkInCounts = await CheckInEligibility.Public(db).Where(r => ids.Contains(r.ShopId))
            .GroupBy(r => r.ShopId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var matchedMenu = f.Menu is null ? null : MatchingMenuItems(f.Menu);
        var positions = matchedMenu is null ? [] : await (
            from m in db.ShopMenus.AsNoTracking() where ids.Contains(m.CoffeeShopId)
            join i in matchedMenu on m.Id equals i.ShopMenuId
            select new { m.CoffeeShopId, m.Currency, m.CapturedAtUtc, i.Price, i.VolumeMl, i.DrinkDefinition!.Slug, i.DrinkDefinition.NameRu, i.DrinkDefinition.Category }
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
                        Price = p.Price, Currency = p.Currency, VolumeMl = p.VolumeMl, CheckedAtUtc = p.CapturedAtUtc
                    }).ToArray()
                };
            }).ToArray(), TotalItems = count, TotalPages = (count + request.PageSize - 1) / request.PageSize, CurrentPage = request.Page, PageSize = request.PageSize
        };
    }
}
