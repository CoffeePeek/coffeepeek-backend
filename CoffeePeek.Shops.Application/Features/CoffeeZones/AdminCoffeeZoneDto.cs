using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public sealed record AdminCoffeeZoneDto(
    Guid Id,
    Guid CityId,
    string Name,
    string? Description,
    decimal CenterLatitude,
    decimal CenterLongitude,
    int RadiusMeters,
    CoffeeZoneStatus Status,
    int ShopCount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    CoffeeZonePoint[] Polygon,
    string Color);
