using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Domain.Entities;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public static class AdminPublishedShopMapper
{
    public static AdminPublishedShopDto Map(DomainCoffeeShop shop, MediaPublicUrlOptions mediaOptions) => new(
        shop.Id,
        shop.Name,
        shop.Location?.CityId ?? Guid.Empty,
        shop.Status,
        shop.Type is null ? null : (CoffeeShopType)(int)shop.Type.Value,
        shop.CreatorId,
        shop.OwnerUserId,
        shop.ModerationId,
        shop.CreatedAtUtc,
        shop.IsHidden,
        shop.ImportedFromFileAt,
        MapPhotos(shop.ShopPhotos, mediaOptions),
        shop.Description,
        (ContractPriceRange)shop.PriceRange,
        shop.Location is null
            ? null
            : new AdminShopLocationDto(
                shop.Location.CityId,
                shop.Location.Address,
                shop.Location.Latitude,
                shop.Location.Longitude),
        shop.Contact is null
            ? null
            : new AdminShopContactsDto(
                shop.Contact.PhoneNumber,
                shop.Contact.Email,
                shop.Contact.SiteLink,
                shop.Contact.InstagramLink),
        shop.Schedules
            .Select(s => new ScheduleDto(
                s.DayOfWeek,
                s.IsClosed,
                s.Intervals?
                    .Select(i => new ShopScheduleIntervalDto
                    {
                        OpenTime = i.OpenTime,
                        CloseTime = i.CloseTime
                    })
                    .ToList() ?? []))
            .ToList(),
        shop.Equipments.Select(e => e.Id).ToList(),
        shop.CoffeeBeans.Select(b => b.Id).ToList(),
        shop.Roasters.Select(r => r.Id).ToList(),
        shop.BrewMethods.Select(m => m.Id).ToList(),
        // DB trigger owns the value; responses right after a write show the pre-write score.
        shop.DataCompletenessScore,
        shop.ShopTags.Select(t => t.TagId).ToList(),
        shop.ShopEquipments.Where(a => a.IsPrimary).Select(a => a.EquipmentId).ToList(),
        shop.ProfileRevision);

    public static IReadOnlyList<AdminShopPhotoDto> MapPhotos(
        IReadOnlyCollection<ShopPhoto> photos,
        MediaPublicUrlOptions mediaOptions) =>
        photos
            .OrderBy(p => p.SortIndex)
            .ThenBy(p => p.CreatedAtUtc)
            .Select(p => new AdminShopPhotoDto(
                p.Id,
                p.FileName,
                p.ContentType,
                p.StorageKey,
                MediaStorageUrlBuilder.BuildPublicUrl(
                    mediaOptions.PublicEndpoint,
                    mediaOptions.ShopBucketName,
                    p.StorageKey) ?? string.Empty,
                p.SizeBytes,
                p.SortIndex))
            .ToList();
}
