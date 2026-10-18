using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Application.Features.Menu;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Persistance.Configuration;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CoffeeShopType = CoffeePeek.Contract.Enums.CoffeeShopType;

namespace CoffeePeek.Shops.Persistance.Queries;

public class CoffeeShopQueries(
    ShopsDbContext context,
    IMapper mapper,
    IQueryCoffeeDrinkRepository drinkRepository,
    IQueryShopMenuRepository menuRepository,
    IOptions<MediaPublicUrlOptions> mediaOptions) : ICoffeeShopQueries
{
    public async Task<(ShortShopDto[] Items, int TotalCount)> Search(SearchCoffeeShopsQuery request, CancellationToken ct)
    {
        var query = context.Shops.AsNoTracking()
            .Where(s => s.Status == CoffeeShopStatus.Active && !s.IsHidden);

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = $"%{request.Query.Trim().ToLowerInvariant().Replace('ё', 'е')}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Name.ToLower().Replace("ё", "е"), term) ||
                EF.Functions.ILike(s.Location.Address.ToLower().Replace("ё", "е"), term));
        }

        if (request.CityId.HasValue)
        {
            query = query.Where(s => s.Location.CityId == request.CityId.Value);
        }

        if (request.Equipments is { Length: > 0 })
        {
            query = query.Where(s => s.ShopEquipments.Any(se => request.Equipments.Contains(se.EquipmentId)));
        }

        if (request.Beans is { Length: > 0 })
        {
            query = query.Where(s =>
                s.CoffeeBeans.Any(cbs => request.Beans.Contains(cbs.Id)));
        }

        if (request.Roasters is { Length: > 0 })
        {
            query = query.Where(s =>
                s.Roasters.Any(rs => request.Roasters.Contains(rs.Id)));
        }

        if (request.BrewMethods is { Length: > 0 })
        {
            query = query.Where(s =>
                s.BrewMethods.Any(sbm => request.BrewMethods.Contains(sbm.Id)));
        }

        if (request.PriceRange.HasValue)
        {
            var priceRangeValue = (int)request.PriceRange.Value;
            query = query.Where(s => (int)s.PriceRange == priceRangeValue);
        }

        if (request.Type.HasValue)
        {
            var typeValue = (CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeFocusType)(int)request.Type.Value;
            query = query.Where(s => s.Type == typeValue);
        }

        if (request.MinRating.HasValue)
        {
            var minRating = request.MinRating.Value;
            var ratingSubquery = CheckInEligibility.LatestRatings(context)
                .GroupBy(r => r.ShopId!.Value)
                .Select(g => new { CoffeeShopId = g.Key, Avg = g.Average(r => r.Rating!.AverageRating) });
            // INNER JOIN: shops with no reviews are excluded when MinRating filter is active — consistent with previous behavior
            query = query
                .Join(ratingSubquery, s => s.Id, r => r.CoffeeShopId, (s, r) => new { Shop = s, r.Avg })
                .Where(x => x.Avg >= minRating)
                .Select(x => x.Shop);
        }

        if (request.Tags is { Length: > 0 })
        {
            foreach (var tagId in request.Tags.Distinct())
            {
                var capturedTagId = tagId;
                query = query.Where(s => s.ShopTags.Any(t => t.TagId == capturedTagId));
            }
        }

        if (request.IsNew.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-BusinessConstants.ItNewEntityInDays);
            query = request.IsNew.Value
                ? query.Where(s => s.CreatedAtUtc >= cutoff)
                : query.Where(s => s.CreatedAtUtc < cutoff);
        }

        if (request.IsVisited.HasValue && request.UserId.HasValue)
        {
            var userId = request.UserId.Value;
            query = request.IsVisited.Value
                ? query.Where(s => context.CheckIns.Any(c => c.ShopId == s.Id && c.UserId == userId && c.DeletedAtUtc == null))
                : query.Where(s => !context.CheckIns.Any(c => c.ShopId == s.Id && c.UserId == userId && c.DeletedAtUtc == null));
        }

        // IsOpen evaluated in UTC (same convention as CoffeeShop.IsOpen domain property).
        if (request.IsOpen.HasValue)
        {
            var now = DateTime.UtcNow;
            var dow = now.DayOfWeek;
            var previousDow = dow == DayOfWeek.Sunday ? DayOfWeek.Saturday : dow - 1;
            var timeOfDay = now.TimeOfDay;

            // A UTC interval with CloseTime < OpenTime crosses midnight. Its first part belongs
            // to the declared UTC day and its tail belongs to the following UTC day.
            if (request.IsOpen.Value)
            {
                query = query.Where(s =>
                    s.Schedules.Any(sch =>
                        sch.DayOfWeek == dow &&
                        !sch.IsClosed &&
                        sch.Intervals.Any(i =>
                            (i.CloseTime >= i.OpenTime && timeOfDay >= i.OpenTime && timeOfDay <= i.CloseTime) ||
                            (i.CloseTime < i.OpenTime && timeOfDay >= i.OpenTime))) ||
                    s.Schedules.Any(sch =>
                        sch.DayOfWeek == previousDow &&
                        !sch.IsClosed &&
                        sch.Intervals.Any(i => i.CloseTime < i.OpenTime && timeOfDay <= i.CloseTime)));
            }
            else
            {
                query = query.Where(s =>
                    s.Schedules.Any() &&
                    !s.Schedules.Any(sch =>
                        sch.DayOfWeek == dow &&
                        !sch.IsClosed &&
                        sch.Intervals.Any(i =>
                            (i.CloseTime >= i.OpenTime && timeOfDay >= i.OpenTime && timeOfDay <= i.CloseTime) ||
                            (i.CloseTime < i.OpenTime && timeOfDay >= i.OpenTime))) &&
                    !s.Schedules.Any(sch =>
                        sch.DayOfWeek == previousDow &&
                        !sch.IsClosed &&
                        sch.Intervals.Any(i => i.CloseTime < i.OpenTime && timeOfDay <= i.CloseTime)));
            }
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .AsSplitQuery()
            .OrderByDescending(x => x.DataCompletenessScore)
            .ThenBy(x => x.Name)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ProjectToType<ShortShopDto>(mapper.Config)
            .ToArrayAsync(ct);

        await PatchOpenAndNewFlagsAsync(items, ct);
        await PatchRatingsAsync(items, ct);

        return (items, totalCount);
    }

    public async Task<CoffeeShopDetailsDto?> GetDetailsById(Guid id, CancellationToken ct)
    {
        var dto = await context.Shops
            .AsNoTracking()
            .AsSplitQuery()
            .Where(x => x.Id == id && !x.IsHidden)
            .ProjectToType<CoffeeShopDetailsDto>(mapper.Config)
            .FirstOrDefaultAsync(ct);

        if (dto is null)
            return null;

        var tags = await context.CoffeeShopTags
            .AsNoTracking()
            .Where(t => t.ShopId == id && t.Tag != null && t.Tag.IsActive)
            .OrderBy(t => t.Tag!.SortOrder)
            .ThenBy(t => t.Tag!.Name)
            .Select(t => new ShopTagDto(
                t.Tag!.Id,
                t.Tag.Slug,
                t.Tag.Name,
                t.Tag.Description,
                t.Tag.SortOrder))
            .ToArrayAsync(ct);

        var state = await context.Shops
            .AsNoTracking()
            .AsSplitQuery()
            .Where(s => s.Id == id)
            .Select(s => new { s.CreatedAtUtc, s.Status, s.Schedules, CoffeeFocus = s.Type })
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        var isNew = state is not null && state.CreatedAtUtc >= now.AddDays(-BusinessConstants.ItNewEntityInDays);
        var isOpen = state is null ? null : ComputeIsOpen(state.Status, state.Schedules, now);

        var catalog = await drinkRepository.GetActiveAsync(ct);
        var menu = await menuRepository.GetByShopIdAsync(id, ct);

        return dto with
        {
            Tags = tags,
            Photos = dto.Photos ?? [],
            IsNew = isNew,
            IsOpen = isOpen,
            Type = (CoffeeShopType?)(int?)state?.CoffeeFocus,
            Menu = ShopMenuDtoFactory.FromShopMenu(menu, catalog, mediaOptions.Value)
        };
    }

    public async Task<GetShopsInBoundsResponse> GetMap(
        GetShopsInBoundsQuery query,
        MapClusteringOptions options,
        CancellationToken ct = default)
    {
        var baseQuery = BuildMapPointsQuery(context, query);

        if (!query.Zoom.HasValue)
        {
            var legacyPoints = await baseQuery.Take(options.MaxResponseItems).ToArrayAsync(ct);
            return new GetShopsInBoundsResponse(legacyPoints.Select(ToMapShop));
        }

        var zoom = query.Zoom.Value;
        // Anonymous endpoint: never materialize the whole table. Marker zooms only need one page (+1 to detect
        // truncation); cluster zooms get a hard ceiling.
        // ponytail: in-memory clustering capped at MaxClusterSourcePoints; move to SQL grid GROUP BY if the catalog outgrows it.
        var points = await baseQuery
            .Take(zoom > options.ZoneMaxZoom ? options.MaxResponseItems + 1 : MaxClusterSourcePoints + 1)
            .ToArrayAsync(ct);
        var sourceTruncated = zoom <= options.ZoneMaxZoom && points.Length > MaxClusterSourcePoints;
        if (sourceTruncated)
            points = points[..MaxClusterSourcePoints];
        if (zoom <= options.ClusterMaxZoom)
        {
            var clusters = MapClusterBuilder.Build(
                points.Select(p => new MapClusterPoint(p.Id, p.Latitude, p.Longitude)),
                zoom,
                options.ClusterCellPixels);
            return new GetShopsInBoundsResponse([])
            {
                Clusters = clusters.Take(options.MaxResponseItems).ToArray(),
                Zones = [],
                IsTruncated = sourceTruncated || clusters.Length > options.MaxResponseItems
            };
        }

        var publishedZones = await context.CoffeeZones.AsNoTracking()
            .Where(z => z.Status == CoffeeZoneStatus.Published)
            .OrderBy(z => z.Id)
            .ToArrayAsync(ct);
        var visibleZones = publishedZones
            .Where(z => CircleIntersectsBounds(z, query))
            .ToArray();

        var cityIds = visibleZones.Select(z => z.CityId).Distinct().ToArray();
        var cityPoints = cityIds.Length == 0
            ? []
            : await context.Shops.AsNoTracking()
                .Where(s => s.Status == CoffeeShopStatus.Active && !s.IsHidden
                            && cityIds.Contains(s.Location.CityId)
                            && s.Location.Latitude.HasValue
                            && s.Location.Longitude.HasValue)
                .OrderBy(s => s.Id)
                .Select(s => new MapPoint(
                    s.Id, s.Location.CityId, s.Location.Latitude!.Value, s.Location.Longitude!.Value,
                    s.Name, (CoffeeShopType?)(int?)s.Type))
                .ToArrayAsync(ct);
        var zoneIds = visibleZones.Select(z => z.Id).ToArray();
        var overrides = zoneIds.Length == 0
            ? []
            : await context.CoffeeZoneMembershipOverrides.AsNoTracking()
                .Where(x => zoneIds.Contains(x.ZoneId))
                .ToArrayAsync(ct);
        var overrideByPair = overrides.ToDictionary(x => (x.ZoneId, x.ShopId));
        var zoneDtos = visibleZones.Select(zone => new MapCoffeeZoneDto(
            zone.Id,
            zone.Name,
            zone.Description,
            zone.CenterLatitude,
            zone.CenterLongitude,
            zone.RadiusMeters,
            cityPoints.Count(point => IsMember(point, zone, overrideByPair)),
            zone.Polygon.Select(p => new MapCoffeeZonePointDto(p.Latitude, p.Longitude)).ToArray(),
            zone.Color))
            .ToArray();

        if (zoom <= options.ZoneMaxZoom)
        {
            var unrepresented = points
                .Where(point => !visibleZones.Any(zone => IsMember(point, zone, overrideByPair)))
                .ToArray();
            var clusters = MapClusterBuilder.Build(
                unrepresented.Select(p => new MapClusterPoint(p.Id, p.Latitude, p.Longitude)),
                zoom,
                options.ClusterCellPixels);
            var availableClusterSlots = Math.Max(0, options.MaxResponseItems - zoneDtos.Length);
            return new GetShopsInBoundsResponse([])
            {
                Zones = zoneDtos.Take(options.MaxResponseItems).ToArray(),
                Clusters = clusters.Take(availableClusterSlots).ToArray(),
                IsTruncated = sourceTruncated || zoneDtos.Length + clusters.Length > options.MaxResponseItems
            };
        }

        var allZoneIds = publishedZones.Select(z => z.Id).ToArray();
        var pointIds = points.Select(p => p.Id).ToArray();
        var primaryOverrides = pointIds.Length == 0
            ? []
            : await context.CoffeeZoneMembershipOverrides.AsNoTracking()
                .Where(x => pointIds.Contains(x.ShopId) && allZoneIds.Contains(x.ZoneId))
                .ToArrayAsync(ct);
        var allOverridesByPair = primaryOverrides.ToDictionary(x => (x.ZoneId, x.ShopId));
        var explicitPrimary = primaryOverrides
            .Where(x => x.Kind == CoffeeZoneMembershipOverrideKind.Primary)
            .ToDictionary(x => x.ShopId, x => x.ZoneId);
        var mapShops = points.Take(options.MaxResponseItems).Select(point =>
        {
            var dto = ToMapShop(point);
            dto.PrimaryZoneId = explicitPrimary.TryGetValue(point.Id, out var zoneId)
                ? zoneId
                : publishedZones
                    .Where(zone => IsMember(point, zone, allOverridesByPair))
                    .OrderBy(zone => GeoDistance.HaversineMeters(
                        zone.CenterLatitude, zone.CenterLongitude, point.Latitude, point.Longitude) / zone.RadiusMeters)
                    .ThenBy(zone => zone.Id)
                    .Select(zone => (Guid?)zone.Id)
                    .FirstOrDefault();
            return dto;
        });
        return new GetShopsInBoundsResponse(mapShops)
        {
            Clusters = [],
            Zones = [],
            IsTruncated = points.Length > options.MaxResponseItems
        };
    }

    private const int MaxClusterSourcePoints = 20_000;

    internal static IQueryable<MapPoint> BuildMapPointsQuery(
        ShopsDbContext context,
        GetShopsInBoundsQuery query) =>
        context.Shops.AsNoTracking()
            .Where(s => s.Status == CoffeeShopStatus.Active && !s.IsHidden &&
                        s.Location.Latitude.HasValue &&
                        s.Location.Longitude.HasValue &&
                        s.Location.Latitude >= query.MinLat &&
                        s.Location.Latitude <= query.MaxLat &&
                        s.Location.Longitude >= query.MinLon &&
                        s.Location.Longitude <= query.MaxLon)
            // Sort entities before projecting into MapPoint. Npgsql cannot translate
            // OrderBy(new MapPoint(...).Id) when ordering is applied after Select.
            .OrderBy(s => s.Id)
            .Select(s => new MapPoint(
                s.Id,
                s.Location.CityId,
                s.Location.Latitude!.Value,
                s.Location.Longitude!.Value,
                s.Name,
                (CoffeeShopType?)(int?)s.Type));

    private static MapShopDto ToMapShop(MapPoint point) => new()
    {
        Id = point.Id,
        Latitude = point.Latitude,
        Longitude = point.Longitude,
        Title = point.Title,
        Type = point.Type
    };

    private static bool IsMember(
        MapPoint point,
        CoffeeZone zone,
        IReadOnlyDictionary<(Guid ZoneId, Guid ShopId), CoffeeZoneMembershipOverride> overrides)
    {
        if (overrides.TryGetValue((zone.Id, point.Id), out var membershipOverride))
            return CoffeeZoneMembershipEvaluator.IsMember(
                point.CityId, point.Latitude, point.Longitude, zone, membershipOverride.Kind);
        return CoffeeZoneMembershipEvaluator.IsMember(
            point.CityId, point.Latitude, point.Longitude, zone, null);
    }

    private static bool CircleIntersectsBounds(CoffeeZone zone, GetShopsInBoundsQuery query)
    {
        var nearestLatitude = Math.Clamp(zone.CenterLatitude, query.MinLat, query.MaxLat);
        var nearestLongitude = Math.Clamp(zone.CenterLongitude, query.MinLon, query.MaxLon);
        return GeoDistance.HaversineMeters(
            zone.CenterLatitude, zone.CenterLongitude, nearestLatitude, nearestLongitude) <= zone.RadiusMeters;
    }

    internal sealed record MapPoint(
        Guid Id,
        Guid CityId,
        decimal Latitude,
        decimal Longitude,
        string Title,
        CoffeeShopType? Type);

    /// <summary>Rating and CheckInCount are ignored by the projection; without this every search result showed 0.</summary>
    private async Task PatchRatingsAsync(ShortShopDto[] items, CancellationToken ct)
    {
        if (items.Length == 0)
            return;

        var ids = items.Select(i => i.Id).ToArray();
        var stats = await CheckInEligibility.LatestRatings(context)
            .AsNoTracking()
            .Where(r => r.ShopId != null && ids.Contains(r.ShopId.Value))
            .GroupBy(r => r.ShopId!.Value)
            .Select(g => new
            {
                ShopId = g.Key,
                Average = g.Average(r => (r.Rating!.Place + r.Rating.Coffee + r.Rating.Service) / 3m)
            })
            .ToDictionaryAsync(x => x.ShopId, ct);
        var counts = await CheckInEligibility.Public(context).Where(c => c.ShopId != null && ids.Contains(c.ShopId.Value))
            .GroupBy(c => c.ShopId!.Value).Select(g => new { ShopId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ShopId, ct);

        foreach (var item in items)
        {
            if (!stats.TryGetValue(item.Id, out var s))
                continue;
            item.Rating = Math.Round(s.Average, 2);
            item.CheckInCount = counts.GetValueOrDefault(item.Id)?.Count ?? 0;
        }
    }

    private async Task PatchOpenAndNewFlagsAsync(ShortShopDto[] items, CancellationToken ct)
    {
        if (items.Length == 0)
            return;

        var ids = items.Select(i => i.Id).ToArray();
        var now = DateTime.UtcNow;
        var newCutoff = now.AddDays(-BusinessConstants.ItNewEntityInDays);

        // Schedules and their owned Intervals are two nested collections — split to avoid the
        // cartesian-product warning/cost of loading them via a single joined query.
        var states = await context.Shops
            .AsNoTracking()
            .AsSplitQuery()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.CreatedAtUtc, s.Status, s.Schedules, CoffeeFocus = s.Type })
            .ToListAsync(ct);

        var byId = states.ToDictionary(s => s.Id);
        foreach (var item in items)
        {
            if (!byId.TryGetValue(item.Id, out var state))
                continue;

            item.IsNew = state.CreatedAtUtc >= newCutoff;
            item.IsOpen = ComputeIsOpen(state.Status, state.Schedules, now);
            item.Type = (CoffeeShopType?)(int?)state.CoffeeFocus;
        }
    }

    private static bool? ComputeIsOpen(
        CoffeeShopStatus status,
        IEnumerable<ShopSchedule> schedules,
        DateTime utcNow)
    {
        if (status != CoffeeShopStatus.Active)
            return false;

        return ShopScheduleEvaluator.IsOpenAtUtc(schedules, utcNow);
    }
}
