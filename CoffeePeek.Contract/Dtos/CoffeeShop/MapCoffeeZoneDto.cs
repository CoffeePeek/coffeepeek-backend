namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public sealed record MapCoffeeZoneDto(
    Guid Id,
    string Name,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    int RadiusMeters,
    int ShopCount);
