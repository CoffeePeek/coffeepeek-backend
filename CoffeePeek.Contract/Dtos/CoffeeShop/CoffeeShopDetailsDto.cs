using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record CoffeeShopDetailsDto
{
    public Guid Id { get; init; }
    public Guid CityId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public ShortPhotoMetadataDto[] Photos { get; init; }

    public decimal Rating { get; init; }
    public int ReviewCount { get; init; }
    public ReviewDto[] Reviews { get; init; } = [];
    public CheckInDto[] UserCheckIns { get; init; } = [];


    public bool IsVisited { get; init; }
    public bool? CanCreateReview { get; init; }
    /// <summary>
    /// null if user for unauthenticated user
    /// </summary>
    public Guid? ExistingReviewId { get; init; }
    public bool IsOpen { get; init; }
    public bool IsNew { get; init; }
    public int DataCompletenessScore { get; init; }
    public PriceRange PriceRange { get; init; }
    public CoffeeShopType? Type { get; init; }

    public LocationDto? Location { get; init; }
    public CoffeeBeansDto[]? CoffeeBeans { get; init; }
    public RoasterDto[]? Roasters { get; init; }
    public EquipmentDto[]? Equipments { get; init; }
    public BrewMethodDto[]? BrewMethods { get; init; }
    public ShopContactDto? ShopContact { get; init; }
    public List<ScheduleDto>? Schedules { get; init; }
    public ShopTagDto[] Tags { get; init; } = [];
    public ShopMenuDto? Menu { get; init; }
}
