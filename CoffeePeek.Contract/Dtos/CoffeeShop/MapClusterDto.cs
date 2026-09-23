namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public sealed record MapBoundsDto(
    decimal MinLatitude,
    decimal MinLongitude,
    decimal MaxLatitude,
    decimal MaxLongitude);

public sealed record MapClusterDto(
    string Id,
    decimal Latitude,
    decimal Longitude,
    int Count,
    MapBoundsDto Bounds);
