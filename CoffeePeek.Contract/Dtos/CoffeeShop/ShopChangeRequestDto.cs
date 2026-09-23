#nullable enable

using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ShopChangeContactsDto(
    string? PhoneNumber,
    string? Email,
    string? SiteLink,
    string? InstagramLink);

public record ShopChangeGalleryDto(
    IReadOnlyList<Guid> RetainedPhotoIds,
    IReadOnlyList<UploadedPhotoDto> NewPhotos);

public record ShopChangeMenuDto(
    IReadOnlyList<UpdateShopMenuItemRequest> Items,
    IReadOnlyList<Guid> RetainedPhotoIds,
    IReadOnlyList<UploadedPhotoDto> NewPhotos);

public record ShopChangePayloadDto(
    string? Description = null,
    ShopChangeContactsDto? Contacts = null,
    ShopChangeGalleryDto? Photos = null,
    IReadOnlyList<Guid>? TagIds = null,
    IReadOnlyList<Guid>? RoasterIds = null,
    IReadOnlyList<Guid>? EquipmentIds = null,
    ShopChangeMenuDto? Menu = null,
    IReadOnlyList<Guid>? BrewMethodIds = null);

public record ShopChangeRequestDto(
    Guid Id,
    Guid ShopId,
    Guid SubmittedByUserId,
    ShopChangeSection Section,
    ShopChangePayloadDto Payload,
    ModerationStatus Status,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public record ShopChangeRequestPageDto(
    IReadOnlyList<ShopChangeRequestDto> Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);
