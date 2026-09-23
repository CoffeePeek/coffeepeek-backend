namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public sealed record MapCoffeeZoneDto(
    Guid Id,
    string Name,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    int RadiusMeters,
    int ShopCount,
    MapCoffeeZonePointDto[] Polygon);

/// <summary>Zone outline vertex. Latitude/Longitude/RadiusMeters on the zone are the polygon's derived centroid and bounding circle.</summary>
public sealed record MapCoffeeZonePointDto(decimal Latitude, decimal Longitude);
