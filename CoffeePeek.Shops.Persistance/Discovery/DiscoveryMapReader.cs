using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using CoffeeShopType = CoffeePeek.Contract.Enums.CoffeeShopType;

namespace CoffeePeek.Shops.Persistance.Discovery;

// Shared map projection: callers supply the eligible shops; geometry and response limits stay identical.
internal sealed class DiscoveryMapReader(ShopsDbContext context)
{
    public async Task<GetShopsInBoundsResponse> ReadAsync(
        IQueryable<CoffeeShop> eligibleShops,
        GetShopsInBoundsQuery query,
        MapClusteringOptions options,
        CancellationToken ct = default,
        bool hideEmptyZones = false)
    {
        var baseQuery = BuildMapPointsQuery(eligibleShops, query);

        if (!query.Zoom.HasValue)
        {
            var legacyPoints = await baseQuery.Take(options.MaxResponseItems).ToArrayAsync(ct);
            return new GetShopsInBoundsResponse(legacyPoints.Select(ToMapShop));
        }

        var zoom = query.Zoom.Value;
        // Never materialize the whole viewport. Marker zooms only need one page (+1 to detect
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
            : await eligibleShops
                .Where(s => cityIds.Contains(s.Location.CityId)
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
            .Where(zone => !hideEmptyZones || zone.ShopCount > 0)
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
        IQueryable<CoffeeShop> eligibleShops,
        GetShopsInBoundsQuery query) =>
        eligibleShops
            .Where(s => s.Location.Latitude.HasValue &&
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

}
