using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicRoasterDetailsDto(PublicAddressDto Address, string Name, string? About,
    LocationDto? Location, RoasterContactDto? Contact, ShortPhotoMetadataDto[] Photos, PublicLinkedShopDto[] Shops)
{
    public int CoffeeShopsCount { get; init; }
    public int CoffeeProductsCount { get; init; }
    public PublicTagDto[] Tags { get; init; } = [];
    public int AvailableCoffeeProducts { get; init; }
    public DateTime? CoffeeCatalogUpdatedAtUtc { get; init; }
}
