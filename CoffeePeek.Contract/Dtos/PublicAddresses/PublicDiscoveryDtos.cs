using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

// Public identities are addresses. These types deliberately have no aggregate GUIDs.
public sealed record PublicAddressDto(string Slug, string CanonicalPath, long Revision, bool IsAlias = false)
{
    public static PublicAddressDto From(PublicAddressMetadata address) =>
        new(address.Slug, address.CanonicalPath, address.Revision, address.IsAlias);
}

public sealed record PublicPage<T>(T[] Items, int TotalItems, int TotalPages, int CurrentPage, int PageSize);
public sealed record PublicCoffeeShopsResponse(PublicShopSummaryDto[] CoffeeShops, int TotalItems, int TotalPages, int CurrentPage, int PageSize);
public sealed record PublicEntityEnvelope<T>(T Data, PublicAddressDto Address);
public sealed record PublicUsernameUpdateResponse(string Username, PublicAddressDto? Address);
public sealed record PublicCityDto(PublicAddressDto Address, string Name);
public sealed record PublicRoasterSummaryDto(PublicAddressDto? Address, string Name, string? PhotoUrl, ShortPhotoMetadataDto? CoverPhoto);
public sealed record PublicLinkedShopDto(PublicAddressDto? Address, string Name, ShortPhotoMetadataDto? CoverPhoto);
public sealed record PublicBeanDto(string Slug, string Name);
public sealed record PublicEquipmentDto(string Slug, string Name, string Brand, string Model, EquipmentCategoryEnum Category);
public sealed record PublicBrewMethodDto(string Slug, string Name, BrewMethodCategoryEnum Category);
public sealed record PublicTagDto(string Slug, string Name, string? Description, int SortOrder);

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

public sealed record PublicShopDetailsDto : PublicShopSummaryDto
{
    public string? Description { get; init; }
    public PublicReviewDto[] Reviews { get; init; } = [];
    public PublicCheckInDto[] UserCheckIns { get; init; } = [];
    public bool? CanCreateReview { get; init; }
    public Guid? ExistingReviewId { get; init; }
    public List<ScheduleDto>? Schedules { get; init; }
    public PublicTagDto[] Tags { get; init; } = [];
    public ShopMenuDto? Menu { get; init; }
}

public sealed record PublicReviewDto
{
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public string? DrinkNameRu { get; init; }
    public string? DrinkNameEn { get; init; }

    public Guid Id { get; init; }
    public PublicAddressDto? Author { get; init; }
    public PublicAddressDto? Shop { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? Header { get; init; }
    public string Comment { get; init; } = string.Empty;
    public RatingDto Rating { get; init; } = new();
    public Guid? ModerationReviewId { get; init; }
    public ICollection<UploadedPhotoDto> Photos { get; init; } = [];
    public DateTime CreatedAtUtc { get; init; }
    public int HelpfulCount { get; init; }
    public bool IsHelpfulByCurrentUser { get; init; }
}

public sealed record PublicCheckInDto
{
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public string? DrinkNameRu { get; init; }
    public string? DrinkNameEn { get; init; }

    public Guid Id { get; init; }
    public PublicAddressDto? Shop { get; init; }
    public string Note { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime VisitedAt { get; init; }
    public Guid? ReviewId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public RatingDto Rating { get; init; } = new();
    public ShortPhotoMetadataDto[] Photos { get; init; } = [];
}

public sealed record PublicRoasterDetailsDto(PublicAddressDto Address, string Name, string? About,
    LocationDto? Location, RoasterContactDto? Contact, ShortPhotoMetadataDto[] Photos, PublicLinkedShopDto[] Shops)
{
    public PublicTagDto[] Tags { get; init; } = [];
    public int AvailableCoffeeProducts { get; init; }
    public DateTime? CoffeeCatalogUpdatedAtUtc { get; init; }
}
public sealed record PublicMapShopDto(PublicAddressDto Address, decimal Latitude, decimal Longitude, string Title,
    CoffeeShopType? Type, PublicAddressDto? PrimaryZone);
public sealed record PublicZoneDto(PublicAddressDto Address, string Name, string? Description,
    decimal Latitude, decimal Longitude, int RadiusMeters, int ShopCount, MapCoffeeZonePointDto[] Polygon, string Color);
public sealed record PublicMapDto(PublicMapShopDto[] Shops, MapClusterDto[] Clusters, PublicZoneDto[] Zones, bool IsTruncated);
