using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public class ShortShopDto
{
    public Guid Id { get; init; }
    public Guid CityId { get; init; }
    public required string Name { get; init; }
    public ShortPhotoMetadataDto[] Photos { get; init; }
    
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }

    public bool IsVisited { get; set; }
    public bool IsNew { get; set; }
    public bool IsOpen { get; set; }
    public int DataCompletenessScore { get; init; }
    
    public PriceRange PriceRange { get; init; }
    public CoffeeShopType? Type { get; set; }
    
    public LocationDto? Location { get; init; }
    public CoffeeBeansDto[]? Beans { get; init; }
    public RoasterDto[]? Roasters { get; init; }
    public EquipmentDto[]? Equipments { get; init; }
    public BrewMethodDto[]? BrewMethods { get; init; }
    public ShopContactDto? ShopContact { get; init; }
}
