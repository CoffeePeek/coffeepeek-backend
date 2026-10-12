using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public sealed record CoffeeZoneCandidateDto(
    decimal CenterLatitude,
    decimal CenterLongitude,
    int ShopCount,
    Guid[] ShopIds,
    CoffeeZonePoint[] Polygon);
