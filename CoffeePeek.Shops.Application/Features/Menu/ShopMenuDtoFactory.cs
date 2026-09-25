using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using ContractCategory = CoffeePeek.Contract.Enums.CoffeeDrinkCategory;
using ContractAvailability = CoffeePeek.Contract.Enums.MenuItemAvailability;
using ContractParseStatus = CoffeePeek.Contract.Enums.MenuParseStatus;
using ContractSource = CoffeePeek.Contract.Enums.MenuItemSource;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Menu;

public static class ShopMenuDtoFactory
{
    public static CoffeeDrinkDefinitionDto ToDto(CoffeeDrinkDefinition drink) =>
        new(drink.Slug, drink.NameRu, drink.NameEn, (ContractCategory)(int)drink.Category, drink.SortOrder);

    public static ShopMenuDto? FromShopMenu(
        ShopMenu? menu,
        IReadOnlyList<CoffeeDrinkDefinition> catalog,
        MediaPublicUrlOptions mediaOptions)
    {
        if (menu is null)
            return null;

        var itemsByDrinkId = menu.Items.ToDictionary(i => i.DrinkDefinitionId);
        var items = catalog
            .Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder)
            .Select(drink =>
            {
                if (!itemsByDrinkId.TryGetValue(drink.Id, out var item))
                {
                    return new ShopMenuItemDto(
                        drink.Slug,
                        drink.NameRu,
                        drink.NameEn,
                        (ContractCategory)(int)drink.Category,
                        ContractAvailability.Unknown,
                        null,
                        menu.Currency,
                        null,
                        ContractSource.Parsed);
                }

                return new ShopMenuItemDto(
                    drink.Slug,
                    drink.NameRu,
                    drink.NameEn,
                    (ContractCategory)(int)drink.Category,
                    (ContractAvailability)(int)item.Availability,
                    item.Price,
                    menu.Currency,
                    item.VolumeMl,
                    (ContractSource)(int)item.Source);
            })
            .ToArray();

        var photos = menu.Photos
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new ShortPhotoMetadataDto
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
            })
            .ToArray();

        return new ShopMenuDto(
            menu.CapturedAtUtc,
            menu.UpdatedAtUtc,
            menu.Currency,
            (ContractParseStatus)(int)menu.ParseStatus,
            menu.ParseError,
            menu.SuggestedPriceRange is null
                ? null
                : (ContractPriceRange)(int)menu.SuggestedPriceRange.Value,
            items,
            photos);
    }
}
