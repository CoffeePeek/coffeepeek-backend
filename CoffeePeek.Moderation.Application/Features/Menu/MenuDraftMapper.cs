using CoffeePeek.Contract.Catalog;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using System.Text.Json;

namespace CoffeePeek.Moderation.Application.Features.Menu;

public static class MenuDraftMapper
{
    public static ShopMenuDto? ToDto(MenuDraft? draft, MediaPublicUrlOptions mediaOptions)
    {
        if (draft is null)
            return null;

        var bySlug = draft.Items.ToDictionary(i => i.Slug, StringComparer.OrdinalIgnoreCase);
        var items = StandardCoffeeDrinks.All.Select(drink =>
        {
            if (!bySlug.TryGetValue(drink.Slug, out var item))
            {
                return new ShopMenuItemDto(
                    drink.Slug,
                    drink.NameRu,
                    drink.NameEn,
                    drink.Category,
                    MenuItemAvailability.Unknown,
                    null,
                    draft.Currency,
                    null,
                    MenuItemSource.Parsed);
            }

            return new ShopMenuItemDto(
                drink.Slug,
                string.IsNullOrWhiteSpace(item.NameRu) ? drink.NameRu : item.NameRu,
                string.IsNullOrWhiteSpace(item.NameEn) ? drink.NameEn : item.NameEn,
                item.Category == 0 ? drink.Category : (CoffeeDrinkCategory)item.Category,
                (MenuItemAvailability)item.Availability,
                item.Price,
                draft.Currency,
                item.VolumeMl,
                item.Source == 0 ? MenuItemSource.Parsed : (MenuItemSource)item.Source);
        }).ToArray();

        var photos = draft.Photos.Select(p => new ShortPhotoMetadataDto
        {
            Id = p.Id,
            FileName = p.FileName,
            StorageKey = p.StorageKey,
            FullUrl = MediaStorageUrlBuilder.BuildPublicUrl(
                mediaOptions.PublicEndpoint,
                mediaOptions.ShopBucketName,
                p.StorageKey) ?? string.Empty,
            Urls = PhotoUrlsDto.Create(mediaOptions, mediaOptions.ShopBucketName, p.StorageKey),
            SortIndex = 0
        }).ToArray();

        return new ShopMenuDto(
            draft.CapturedAtUtc,
            draft.UpdatedAtUtc,
            draft.Currency,
            (MenuParseStatus)draft.ParseStatus,
            draft.ParseError,
            draft.SuggestedPriceRange is null ? null : (PriceRange)draft.SuggestedPriceRange.Value,
            items,
            photos);
    }

    public static ShopMenuSnapshot? ToSnapshot(MenuDraft? draft)
    {
        if (draft is null)
            return null;

        return new ShopMenuSnapshot(
            draft.CapturedAtUtc,
            draft.Currency,
            draft.SuggestedPriceRange is null ? null : (PriceRange)draft.SuggestedPriceRange.Value,
            draft.Items.Select(i => new ShopMenuItemSnapshot(
                i.Slug,
                (MenuItemAvailability)i.Availability,
                i.Price,
                i.VolumeMl,
                i.Source == 0 ? MenuItemSource.Parsed : (MenuItemSource)i.Source)).ToArray(),
            draft.Photos.Select(p => new ShopMenuPhotoSnapshot(
                p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.MediaPhotoId)).ToArray(),
            draft.Unmatched.Count == 0
                ? null
                : JsonSerializer.Serialize(draft.Unmatched.Select(u =>
                    new UnmatchedMenuItemDto(u.RawName, u.Price, u.Confidence))));
    }

    public static IReadOnlyList<MenuDraftItem> ToDraftItems(IReadOnlyList<ParsedMenuItemDto> items) =>
        items.Select(i =>
        {
            var catalog = StandardCoffeeDrinks.All.FirstOrDefault(d =>
                string.Equals(d.Slug, i.Slug, StringComparison.OrdinalIgnoreCase));
            return new MenuDraftItem
            {
                Slug = i.Slug,
                NameRu = i.NameRu ?? catalog?.NameRu ?? i.RawName,
                NameEn = i.NameEn ?? catalog?.NameEn ?? i.Slug,
                Category = (int)(i.Category ?? catalog?.Category ?? CoffeeDrinkCategory.Espresso),
                Availability = (int)MenuItemAvailability.Present,
                Price = i.Price,
                VolumeMl = i.VolumeMl,
                Source = (int)MenuItemSource.Parsed
            };
        }).ToArray();

    public static IReadOnlyList<MenuDraftUnmatched> ToDraftUnmatched(IReadOnlyList<UnmatchedMenuItemDto> items) =>
        items.Select(u => new MenuDraftUnmatched
        {
            RawName = u.RawName,
            Price = u.Price,
            Confidence = u.Confidence
        }).ToArray();

    public static IReadOnlyList<MenuDraftItem> ToDraftItems(IReadOnlyList<UpdateShopMenuItemRequest> items) =>
        items.Select(i =>
        {
            var catalog = StandardCoffeeDrinks.All.FirstOrDefault(d =>
                string.Equals(d.Slug, i.Slug, StringComparison.OrdinalIgnoreCase));
            return new MenuDraftItem
            {
                Slug = i.Slug,
                NameRu = catalog?.NameRu ?? i.Slug,
                NameEn = catalog?.NameEn ?? i.Slug,
                Category = (int)(catalog?.Category ?? CoffeeDrinkCategory.Espresso),
                Availability = (int)i.Availability,
                Price = i.Price,
                VolumeMl = i.VolumeMl,
                Source = (int)MenuItemSource.Manual
            };
        }).ToArray();
}
