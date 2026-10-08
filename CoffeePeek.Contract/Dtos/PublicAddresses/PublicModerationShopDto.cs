using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicModerationShopDto
{
    // A submission is a workflow resource, not a published shop identity.
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Address { get; init; }
    public bool AddressIsValidated { get; init; }
    public string? Description { get; init; }
    public PriceRange PriceRange { get; init; }
    public CoffeeShopType? Type { get; init; }
    public PublicAddressDto? City { get; init; }
    public ModerationStatus ModerationStatus { get; init; }
    public string? RejectedReason { get; init; }
    public PublicAddressDto? PublishedShop { get; init; }
    public ShopContactDto? ShopContact { get; init; }
    public List<ScheduleDto>? Schedules { get; init; }
    public string[] Equipments { get; init; } = [];
    public string[] Beans { get; init; } = [];
    public string[] Roasters { get; init; } = [];
    public string[] BrewMethods { get; init; } = [];
    public List<ShortPhotoMetadataDto>? ShopPhotos { get; init; }
    public ShopMenuDto? Menu { get; init; }
    public int DataCompletenessScore { get; init; }
}
