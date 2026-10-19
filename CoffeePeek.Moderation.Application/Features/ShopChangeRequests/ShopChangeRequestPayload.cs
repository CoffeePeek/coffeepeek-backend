using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Admin.Audit;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using DomainSection = CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate.ShopChangeSection;
using DomainStatus = CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus;
using ShopChangeSection = CoffeePeek.Contract.Enums.ShopChangeSection;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;
public static class ShopChangeRequestPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(ShopChangeSection section, ShopChangePayloadDto payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Validate(section, payload);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static ShopChangePayloadDto Deserialize(string json) =>
        JsonSerializer.Deserialize<ShopChangePayloadDto>(json, JsonOptions)
        ?? throw new JsonException("Stored shop change payload is invalid.");

    public static ShopChangeRequestDto Map(ShopChangeRequest request) => new(
        request.Id,
        request.ShopId,
        request.SubmittedByUserId,
        (ShopChangeSection)request.Section,
        Deserialize(request.PayloadJson),
        (ModerationStatus)request.Status,
        request.ReviewedByUserId,
        request.ReviewedAtUtc,
        request.RejectionReason,
        request.CreatedAtUtc,
        request.UpdatedAtUtc);

    private static void Validate(ShopChangeSection section, ShopChangePayloadDto payload)
    {
        if (section != ShopChangeSection.Description && payload.Description is not null)
            throw new ArgumentException("Payload contains unrelated section fields.");

        var otherCount = (payload.Contacts is not null ? 1 : 0)
                         + (payload.Photos is not null ? 1 : 0)
                         + (payload.TagIds is not null ? 1 : 0)
                         + (payload.RoasterIds is not null ? 1 : 0)
                         + (payload.EquipmentIds is not null ? 1 : 0)
                         + (payload.Menu is not null ? 1 : 0)
                         + (payload.BrewMethodIds is not null ? 1 : 0);

        switch (section)
        {
            case ShopChangeSection.Description:
                if (otherCount != 0) throw new ArgumentException("Description request contains unrelated fields.");
                if (payload.Description?.Length > 1000) throw new ArgumentException("Description cannot exceed 1000 characters.");
                break;
            case ShopChangeSection.Contacts:
                RequireOnly(payload.Contacts, otherCount, 1, "Contacts");
                ValidateContacts(payload.Contacts!);
                break;
            case ShopChangeSection.Photos:
                RequireOnly(payload.Photos, otherCount, 1, "Photos");
                ValidateGallery(payload.Photos!);
                break;
            case ShopChangeSection.Tags:
                RequireIds(payload.TagIds, otherCount, "TagIds");
                break;
            case ShopChangeSection.Roasters:
                RequireIds(payload.RoasterIds, otherCount, "RoasterIds");
                break;
            case ShopChangeSection.Equipment:
                RequireIds(payload.EquipmentIds, otherCount, "EquipmentIds");
                break;
            case ShopChangeSection.BrewMethods:
                RequireIds(payload.BrewMethodIds, otherCount, "BrewMethodIds");
                break;
            case ShopChangeSection.Menu:
                RequireOnly(payload.Menu, otherCount, 1, "Menu");
                ValidateMenu(payload.Menu!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(section));
        }
    }

    private static void RequireOnly(object? value, int populatedCount, int expectedCount, string field)
    {
        if (value is null) throw new ArgumentException($"{field} is required for this section.");
        if (populatedCount != expectedCount) throw new ArgumentException("Payload contains unrelated section fields.");
    }

    private static void RequireIds(IReadOnlyList<Guid>? ids, int populatedCount, string field)
    {
        RequireOnly(ids, populatedCount, 1, field);
        var values = ids!;
        if (values.Any(x => x == Guid.Empty)) throw new ArgumentException($"{field} cannot contain empty IDs.");
        if (values.Distinct().Count() != values.Count) throw new ArgumentException($"{field} cannot contain duplicates.");
    }

    private static void ValidateContacts(ShopChangeContactsDto contacts)
    {
        if (contacts.PhoneNumber?.Length > CoffeePeek.Moderation.Domain.BusinessConstants.MaxShopContactPhoneNumberLength)
            throw new ArgumentException("Phone number is too long.");
        if (contacts.Email?.Length > CoffeePeek.Moderation.Domain.BusinessConstants.MaxShopContactEmailLength)
            throw new ArgumentException("Email is too long.");
        if (contacts.SiteLink?.Length > CoffeePeek.Moderation.Domain.BusinessConstants.MaxShopContactSiteLinkLength)
            throw new ArgumentException("Site link is too long.");
        if (contacts.InstagramLink?.Length > CoffeePeek.Moderation.Domain.BusinessConstants.MaxShopContactInstagramLinkLength)
            throw new ArgumentException("Instagram link is too long.");
    }

    private static void ValidateGallery(ShopChangeGalleryDto gallery)
    {
        ArgumentNullException.ThrowIfNull(gallery.RetainedPhotoIds);
        ArgumentNullException.ThrowIfNull(gallery.NewPhotos);
        if (gallery.RetainedPhotoIds.Distinct().Count() != gallery.RetainedPhotoIds.Count)
            throw new ArgumentException("Retained photo IDs cannot contain duplicates.");
        ValidatePhotos(gallery.NewPhotos);
    }

    private static void ValidateMenu(ShopChangeMenuDto menu)
    {
        if (menu.ExpectedInputRevision is null or < 0)
            throw new ArgumentException("Expected menu revision is required and cannot be negative.");
        ArgumentNullException.ThrowIfNull(menu.Items);
        CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate.MenuDraft.CreateEmpty()
            .ApplyManualItems(Features.Menu.MenuDraftMapper.ToDraftItems(menu.Items), DateTime.UtcNow);
        ArgumentNullException.ThrowIfNull(menu.RetainedPhotoIds);
        ArgumentNullException.ThrowIfNull(menu.NewPhotos);
        if (menu.Items.Select(x => x.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() != menu.Items.Count)
            throw new ArgumentException("Menu item slugs cannot contain duplicates.");
        if (menu.RetainedPhotoIds.Distinct().Count() != menu.RetainedPhotoIds.Count)
            throw new ArgumentException("Retained menu photo IDs cannot contain duplicates.");
        ValidatePhotos(menu.NewPhotos);
    }

    private static void ValidatePhotos(IReadOnlyList<CoffeePeek.Contract.Dtos.UploadedPhotoDto> photos)
    {
        if (photos.Select(x => x.StorageKey).Distinct(StringComparer.Ordinal).Count() != photos.Count)
            throw new ArgumentException("New photo storage keys cannot contain duplicates.");
        if (photos.Any(x => string.IsNullOrWhiteSpace(x.FileName)
                            || string.IsNullOrWhiteSpace(x.ContentType)
                            || string.IsNullOrWhiteSpace(x.StorageKey)
                            || x.Size <= 0))
            throw new ArgumentException("Every new photo must contain valid upload metadata.");
    }
}
