using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public record PublicShopSummaryDto
{
    public PublicAddressDto Address { get; init; } = null!;
    public PublicAddressDto? City { get; init; }
    public string Name { get; init; } = string.Empty;
    public ShortPhotoMetadataDto[] Photos { get; init; } = [];
    public decimal Rating { get; init; }
    public int ReviewCount { get; init; }
    public bool IsVisited { get; init; }
    public bool IsNew { get; init; }
    public bool IsOpen { get; init; }
    public int DataCompletenessScore { get; init; }
    public PriceRange PriceRange { get; init; }
    public CoffeeShopType? Type { get; init; }
    public LocationDto? Location { get; init; }
    public PublicBeanDto[] Beans { get; init; } = [];
    public PublicRoasterSummaryDto[] Roasters { get; init; } = [];
    public PublicEquipmentDto[] Equipments { get; init; } = [];
    public PublicBrewMethodDto[] BrewMethods { get; init; } = [];
    public ShopContactDto? ShopContact { get; init; }
}
