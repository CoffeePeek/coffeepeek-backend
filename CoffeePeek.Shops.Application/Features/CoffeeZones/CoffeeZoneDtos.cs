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
    DateTime? UpdatedAtUtc);

public sealed record CoffeeZoneMemberDto(
    Guid ShopId,
    string Name,
    decimal Latitude,
    decimal Longitude,
    double DistanceMeters,
    bool IsAutomatic,
    CoffeeZoneMembershipOverrideKind? OverrideKind,
    bool IsPrimary);

public sealed record CoffeeZoneMembershipPreviewDto(
    AdminCoffeeZoneDto Zone,
    CoffeeZoneMemberDto[] Members);

public sealed record CoffeeZoneCandidateDto(
    decimal CenterLatitude,
    decimal CenterLongitude,
    int SuggestedRadiusMeters,
    int ShopCount,
    Guid[] ShopIds);
