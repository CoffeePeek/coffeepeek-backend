using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicShopChangePayloadDto(
    string? Description = null, ShopChangeContactsDto? Contacts = null, ShopChangeGalleryDto? Photos = null,
    string[]? Tags = null, string[]? Roasters = null, string[]? Equipments = null,
    ShopChangeMenuDto? Menu = null, string[]? BrewMethods = null);

public sealed record PublicShopChangeRequestDto(Guid Id, PublicAddressDto? Shop, ShopChangeSection Section,
    PublicShopChangePayloadDto Payload, ModerationStatus Status, DateTime? ReviewedAtUtc, string? RejectionReason,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

public sealed record PublicModerationReviewDto
{
    public Guid Id { get; init; }
    public string? Header { get; init; }
    public string Comment { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public PublicAddressDto? Shop { get; init; }
    public RatingDto Rating { get; init; } = new();
    public string? RejectedReason { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ModeratedAt { get; init; }
    public ModerationStatus ModerationStatus { get; init; }
    public ShortPhotoMetadataDto[] Photos { get; init; } = [];
}

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

public sealed record PublicModerationRoasterDto(Guid Id, string Name, string? About, ModerationStatus ModerationStatus,
    string? RejectedReason, PublicAddressDto? City, CoffeePeek.Contract.Dtos.Shop.LocationDto? Location,
    CoffeePeek.Contract.Dtos.Shop.RoasterContactDto? Contact, ShortPhotoMetadataDto[] Photos);
