using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicZoneDto(PublicAddressDto Address, string Name, string? Description,
    decimal Latitude, decimal Longitude, int RadiusMeters, int ShopCount, MapCoffeeZonePointDto[] Polygon, string Color);
