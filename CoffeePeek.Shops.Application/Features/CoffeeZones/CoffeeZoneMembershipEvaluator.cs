using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class CoffeeZoneMembershipEvaluator
{
    public static bool IsMember(
        Guid shopCityId,
        decimal shopLatitude,
        decimal shopLongitude,
        CoffeeZone zone,
        CoffeeZoneMembershipOverrideKind? overrideKind)
    {
        return overrideKind switch
        {
            CoffeeZoneMembershipOverrideKind.Exclude => false,
            CoffeeZoneMembershipOverrideKind.Include or CoffeeZoneMembershipOverrideKind.Primary => true,
            _ => shopCityId == zone.CityId && DistanceMeters(shopLatitude, shopLongitude, zone) <= zone.RadiusMeters
        };
    }

    public static double NormalizedDistance(
        decimal shopLatitude,
        decimal shopLongitude,
        CoffeeZone zone) => DistanceMeters(shopLatitude, shopLongitude, zone) / zone.RadiusMeters;

    public static double DistanceMeters(
        decimal shopLatitude,
        decimal shopLongitude,
        CoffeeZone zone) => GeoDistance.HaversineMeters(
        zone.CenterLatitude, zone.CenterLongitude, shopLatitude, shopLongitude);
}
