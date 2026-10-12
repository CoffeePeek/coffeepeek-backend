using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public sealed record CoffeeZoneMemberDto(
    Guid ShopId,
    string Name,
    decimal Latitude,
    decimal Longitude,
    double DistanceMeters,
    bool IsAutomatic,
    CoffeeZoneMembershipOverrideKind? OverrideKind,
    bool IsPrimary);
