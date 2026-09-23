using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ShopDto 
{
    public Guid Id { get; set; }
    public Guid CityId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public PhotoMetadataDto[] Photos { get; set; }
    
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public ReviewDto[] Reviews { get; set; }

    public bool IsVisited { get; init; }
    public bool IsNew { get; init; }
    public bool IsOpen { get; init; }
    public int DataCompletenessScore { get; set; }
    
    public PriceRange PriceRange { get; set; }
    public CoffeeShopType? Type { get; set; }
    
    public LocationDto? Location { get; set; }
    public CoffeeBeansDto[]? CoffeeBeans { get; set; }
    public RoasterDto[]? Roasters { get; set; }
    public EquipmentDto[]? Equipments { get; set; }
    public BrewMethodDto[]? BrewMethods { get; set; }
    public ShopContactDto? ShopContact { get; set; }
    public ScheduleDto[]? Schedules { get; set; }
    public ShopMenuDto? Menu { get; set; }
}
