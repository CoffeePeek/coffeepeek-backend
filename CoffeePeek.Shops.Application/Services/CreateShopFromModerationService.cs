using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Microsoft.Extensions.Logging;
using Wolverine;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Services;

public class CreateShopFromModerationService(
    IQueryCoffeeShopRepository shopRepository,
    IQueryCoffeeBeanRepository coffeeBeanRepository,
    IQueryEquipmentRepository equipmentRepository,
    IQueryRoasterRepository roasterRepository,
    IQueryBrewMethodRepository brewMethodRepository,
    IApplyShopMenuService applyMenu,
    IMessageBus bus,
    ILogger<CreateShopFromModerationService> logger) : ICreateShopFromModerationService
{
    public async Task<Guid> CreateShopFromApprovedEventAsync(ShopDto shopDto, Guid? creatorId, Guid moderationId, CancellationToken cancellationToken = default)
    {
        var existingId = await shopRepository.GetIdByModerationId(moderationId, cancellationToken);
        if (existingId.HasValue)
        {
            logger.LogInformation(
                "Shop {ShopId} already exists for moderation {ModerationId}",
                existingId.Value,
                moderationId);
            return existingId.Value;
        }

        var priceRange = shopDto.PriceRange != 0
            ? (DomainPriceRange)shopDto.PriceRange
            : shopDto.Menu?.SuggestedPriceRange is { } suggested
                ? (DomainPriceRange)(int)suggested
                : DomainPriceRange.Moderate;

        var shop = new CoffeeShop(creatorId, shopDto.Name, shopDto.Description, priceRange, moderationId);

        if (shopDto.Type.HasValue)
            shop.SetCoffeeFocus((CoffeeFocusType)(int)shopDto.Type.Value);

        if (shopDto.Location != null)
        {
            // A location without coordinates used to throw on Latitude!.Value, dead-lettering the approval while
            // Moderation waited forever for the completion reply.
            if (shopDto.Location is { Latitude: { } lat, Longitude: { } lon })
                shop.SetLocation(shopDto.CityId, shopDto.Location.Address, lat, lon);
            else
                shop.SetDraftLocation(shopDto.CityId, shopDto.Location.Address);
        }

        if (shopDto.ShopContact != null)
        {
            shop.SetContact(shopDto.ShopContact.InstagramLink, shopDto.ShopContact.Email, shopDto.ShopContact.SiteLink, shopDto.ShopContact.PhoneNumber);
        }

        if (shopDto.Equipments is { Length: > 0 })
        {
            var ids = shopDto.Equipments.Select(x => x.Id).ToList();
            var equipments = await equipmentRepository.GetByIds(ids, cancellationToken);
            foreach (var equipment in equipments)
            {
                shop.AddEquipment(equipment);
            }
        }

        if (shopDto.BrewMethods is { Length: > 0 })
        {
            var ids = shopDto.BrewMethods.Select(x => x.Id).ToList();
            var brewMethods = await brewMethodRepository.GetByIds(ids, cancellationToken);
            shop.SetBrewMethods(brewMethods);
        }

        if (shopDto.Roasters is { Length: > 0 })
        {
            var ids = shopDto.Roasters.Select(x => x.Id).ToList();
            var roasters = await roasterRepository.GetByIds(ids, cancellationToken);
            shop.SetRoasters(roasters);
        }

        if (shopDto.CoffeeBeans is { Length: > 0 })
        {
            var ids = shopDto.CoffeeBeans.Select(x => x.Id).ToList();
            var coffeeBeans = await coffeeBeanRepository.GetByIds(ids, cancellationToken);
            shop.SetBeans(coffeeBeans);
        }

        if (shopDto.Schedules is { Length: > 0 })
        {
            var schedules = shopDto.Schedules
                .Select(x => 
                    ShopSchedule.Create(x.DayOfWeek, x.IsClosed, x.Intervals
                        .Select(i => ShopScheduleInterval.Create(i.OpenTime, i.CloseTime)).ToList()))
                .ToList();
            shop.AddSchedule(schedules);
        }

        if (shopDto.Photos is { Length: > 0 })
        {
            var photos = shopDto.Photos.Select(p => new ShopPhoto(
                p.FileName,
                p.ContentType,
                p.StorageKey,
                p.SizeBytes,
                p.OwnerId)).ToArray();

            shop.AddPhotos(photos);
        }

        shopRepository.Add(shop);
        if (shopDto.Menu is not null)
        {
            await applyMenu.ApplySnapshotAsync(
                shop.Id,
                ToSnapshot(shopDto.Menu),
                applySuggestedPriceRange: false,
                creatorId,
                cancellationToken);
        }

        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));
        if (!shop.IsHidden && shop.Location is { Latitude: not null, Longitude: not null })
            await bus.PublishAsync(new CoffeePeek.Contract.Events.Shops.ShopPublished(shop.Id, shop.Name, DateTime.UtcNow));

        logger.LogInformation("Prepared shop {ShopId} from moderation event {ModerationId} for transaction commit", shop.Id,
            moderationId);

        return shop.Id;
    }

    private static ShopMenuSnapshot ToSnapshot(ShopMenuDto menu) =>
        new(
            menu.CapturedAtUtc,
            menu.Currency,
            menu.SuggestedPriceRange,
            menu.Items
                .Where(i => i.Availability != MenuItemAvailability.Unknown)
                .Select(i => new ShopMenuItemSnapshot(
                    i.Slug, i.Availability, i.Price, i.Source))
                .ToArray(),
            menu.Photos
                .Select(p => new ShopMenuPhotoSnapshot(p.FileName, "image/jpeg", p.StorageKey, 0))
                .ToArray());
}

