using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

public sealed class CoffeeZoneQueries(ShopsDbContext context) : ICoffeeZoneQueries
{
    public async Task<AdminCoffeeZoneDto[]> GetAllAsync(Guid? cityId, CancellationToken ct = default)
    {
        var zoneQuery = context.CoffeeZones.AsNoTracking();
        if (cityId.HasValue)
            zoneQuery = zoneQuery.Where(z => z.CityId == cityId.Value);

        var zones = await zoneQuery.OrderBy(z => z.Name).ToArrayAsync(ct);
        if (zones.Length == 0)
            return [];

        var cityIds = zones.Select(z => z.CityId).Distinct().ToArray();
        var shops = await LoadActiveShopsAsync(cityIds, ct);
        var zoneIds = zones.Select(z => z.Id).ToArray();
        var overrides = await context.CoffeeZoneMembershipOverrides.AsNoTracking()
            .Where(x => zoneIds.Contains(x.ZoneId))
            .ToArrayAsync(ct);

        return zones.Select(z => ToDto(z, CountMembers(z, shops, overrides))).ToArray();
    }

    public async Task<AdminCoffeeZoneDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var zone = await context.CoffeeZones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null)
            return null;

        var shops = await LoadActiveShopsAsync([zone.CityId], ct);
        var overrides = await context.CoffeeZoneMembershipOverrides.AsNoTracking()
            .Where(x => x.ZoneId == id)
            .ToArrayAsync(ct);
        return ToDto(zone, CountMembers(zone, shops, overrides));
    }

    public async Task<CoffeeZoneMembershipPreviewDto?> PreviewMembershipAsync(Guid id, CancellationToken ct = default)
    {
        var zone = await context.CoffeeZones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null)
            return null;

        var zones = await context.CoffeeZones.AsNoTracking()
            .Where(z => z.CityId == zone.CityId && (z.Status == CoffeeZoneStatus.Published || z.Id == zone.Id))
            .ToArrayAsync(ct);
        var shops = await LoadActiveShopsAsync([zone.CityId], ct);
        var zoneIds = zones.Select(z => z.Id).ToArray();
        var overrides = await context.CoffeeZoneMembershipOverrides.AsNoTracking()
            .Where(x => zoneIds.Contains(x.ZoneId))
            .ToArrayAsync(ct);
        var overridesByPair = overrides.ToDictionary(x => (x.ZoneId, x.ShopId));
        var explicitPrimaryByShop = overrides
            .Where(x => x.Kind == CoffeeZoneMembershipOverrideKind.Primary)
            .ToDictionary(x => x.ShopId, x => x.ZoneId);

        var members = new List<CoffeeZoneMemberDto>();
        foreach (var shop in shops.Where(s => s.CityId == zone.CityId))
        {
            overridesByPair.TryGetValue((zone.Id, shop.Id), out var membershipOverride);
            var distance = CoffeeZoneMembershipEvaluator.DistanceMeters(shop.Latitude, shop.Longitude, zone);
            var automatic = zone.Contains(shop.Latitude, shop.Longitude);
            var isMember = CoffeeZoneMembershipEvaluator.IsMember(
                shop.CityId, shop.Latitude, shop.Longitude, zone, membershipOverride?.Kind);
            if (!isMember)
                continue;

            var isPrimary = explicitPrimaryByShop.TryGetValue(shop.Id, out var explicitZoneId)
                ? explicitZoneId == zone.Id
                : FindAutomaticPrimaryZone(shop, zones, overridesByPair)?.Id == zone.Id;
            members.Add(new CoffeeZoneMemberDto(
                shop.Id, shop.Name, shop.Latitude, shop.Longitude, distance,
                automatic, membershipOverride?.Kind, isPrimary));
        }

        var ordered = members.OrderBy(m => m.DistanceMeters).ThenBy(m => m.ShopId).ToArray();
        return new CoffeeZoneMembershipPreviewDto(ToDto(zone, ordered.Length), ordered);
    }

    public async Task<CoffeeZoneCandidateDto[]> GenerateCandidatesAsync(
        Guid cityId,
        int radiusMeters,
        int minShops,
        CancellationToken ct = default)
    {
        var shops = await LoadActiveShopsAsync([cityId], ct);
        return CoffeeZoneCandidateGenerator.Generate(
            shops.Select(s => new CoffeeZoneCandidatePoint(s.Id, s.Latitude, s.Longitude)),
            radiusMeters,
            minShops);
    }

    private async Task<ZoneShopPoint[]> LoadActiveShopsAsync(Guid[] cityIds, CancellationToken ct) =>
        await context.Shops.AsNoTracking()
            .Where(s => s.Status == CoffeeShopStatus.Active && !s.IsHidden
                        && cityIds.Contains(s.Location.CityId)
                        && s.Location.Latitude.HasValue
                        && s.Location.Longitude.HasValue)
            .Select(s => new ZoneShopPoint(
                s.Id, s.Name, s.Location.CityId,
                s.Location.Latitude!.Value, s.Location.Longitude!.Value))
            .ToArrayAsync(ct);

    private static int CountMembers(
        CoffeeZone zone,
        IReadOnlyCollection<ZoneShopPoint> shops,
        IReadOnlyCollection<CoffeeZoneMembershipOverride> overrides)
    {
        var byShop = overrides.Where(x => x.ZoneId == zone.Id).ToDictionary(x => x.ShopId);
        return shops.Count(shop =>
        {
            if (shop.CityId != zone.CityId && !byShop.ContainsKey(shop.Id))
                return false;
            if (byShop.TryGetValue(shop.Id, out var membershipOverride))
                return membershipOverride.Kind != CoffeeZoneMembershipOverrideKind.Exclude;
            return CoffeeZoneMembershipEvaluator.IsMember(
                shop.CityId, shop.Latitude, shop.Longitude, zone, null);
        });
    }

    private static CoffeeZone? FindAutomaticPrimaryZone(
        ZoneShopPoint shop,
        IEnumerable<CoffeeZone> zones,
        IReadOnlyDictionary<(Guid ZoneId, Guid ShopId), CoffeeZoneMembershipOverride> overrides)
    {
        return zones
            .Where(zone =>
            {
                if (overrides.TryGetValue((zone.Id, shop.Id), out var membershipOverride))
                    return membershipOverride.Kind != CoffeeZoneMembershipOverrideKind.Exclude;
                return CoffeeZoneMembershipEvaluator.IsMember(
                    shop.CityId, shop.Latitude, shop.Longitude, zone, null);
            })
            .OrderBy(zone => CoffeeZoneMembershipEvaluator.NormalizedDistance(
                shop.Latitude, shop.Longitude, zone))
            .ThenBy(zone => zone.Id)
            .FirstOrDefault();
    }

    private static AdminCoffeeZoneDto ToDto(CoffeeZone zone, int shopCount) => new(
        zone.Id, zone.CityId, zone.Name, zone.Description,
        zone.CenterLatitude, zone.CenterLongitude, zone.RadiusMeters,
        zone.Status, shopCount, zone.CreatedAtUtc, zone.UpdatedAtUtc, zone.Polygon.ToArray(), zone.Color);

    private sealed record ZoneShopPoint(Guid Id, string Name, Guid CityId, decimal Latitude, decimal Longitude);
}
