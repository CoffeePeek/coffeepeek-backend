using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events;
using CoffeePeek.Moderation.Application.Features.Geocoding;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;
using Wolverine;
using DomainCoffeeFocus = CoffeePeek.Moderation.Domain.Aggregates.Enums.CoffeeFocus;
using DomainPriceRange = CoffeePeek.Moderation.Domain.Aggregates.Enums.PriceRange;

namespace CoffeePeek.Moderation.Application.Features.Shop.UpdateShop;

public static class UpdateModerationCoffeeShopHandler
{
    public static async Task<UpdateEntityResponse<ModerationShopDto>> Handle(
        UpdateModerationCoffeeShopCommand command,
        IModerationShopRepository repository,
        IMapper mapper,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var moderationShopDto = command.ModerationShopDto;
        // Full load is required: UpdateRelations and photo removal diff against the current collections,
        // and with them unloaded every edit duplicated join rows and never removed anything.
        var shop = await repository.GetByIdAsync(command.ModerationShopDto.Id, ct);

        if (shop == null || (!command.IsPrivilegedModerator && shop.UserId != command.UserId))
        {
            return UpdateEntityResponse<ModerationShopDto>.Error("Shop not found or access denied");
        }

        // Shops ignores re-approval of an already published shop, so edits here would be silently lost.
        if (shop.ModerationStatus == ModerationStatus.Approved)
        {
            return UpdateEntityResponse<ModerationShopDto>.Error(
                "Approved shop is already published; submit a shop change request instead");
        }

        // shortcut: the legacy DTO encodes an unspecified range as 0; make it nullable in a versioned API.
        var domainPriceRange = moderationShopDto.PriceRange == 0
            ? (DomainPriceRange?)null
            : (DomainPriceRange)moderationShopDto.PriceRange;
        var domainCoffeeFocus = moderationShopDto.Type != null
            ? (DomainCoffeeFocus?)moderationShopDto.Type
            : null;
        var cityChanged = shop.CityId != moderationShopDto.CityId;
        shop.UpdateInfo(
            moderationShopDto.Name,
            moderationShopDto.Description,
            domainPriceRange,
            moderationShopDto.CityId,
            domainCoffeeFocus);

        var submittedAddress = moderationShopDto.Address ?? shop.Location?.Address;
        if (submittedAddress != null &&
            (cityChanged || submittedAddress != shop.Location?.Address || shop.Location is { IsAddressValidated: false }))
        {
            shop.SetLocation(new ModerationLocation(submittedAddress));
            await bus.PublishAsync(new ModerationGeocodingRequested(
                ModerationGeocodingKind.Shop, shop.Id, submittedAddress));
        }

        if (moderationShopDto.ShopContact != null)
        {
            shop.UpdateContacts(
                moderationShopDto.ShopContact.PhoneNumber,
                moderationShopDto.ShopContact.InstagramLink,
                moderationShopDto.ShopContact.Email,
                moderationShopDto.ShopContact.SiteLink);
        }

        if (moderationShopDto.Schedules != null)
            shop.UpdateSchedules(moderationShopDto.Schedules.Select(s =>
                (s.DayOfWeek, s.IsClosed,
                    s.IsClosed
                        ? new List<(TimeSpan OpenTime, TimeSpan CloseTime)>()
                        : (s.Intervals ?? []).Select(i => (i.OpenTime, i.CloseTime)).ToList())));

        shop.UpdateRelations(
            moderationShopDto.EquipmentIds,
            moderationShopDto.CoffeeBeanIds,
            moderationShopDto.RoasterIds,
            moderationShopDto.BrewMethodIds);

        // Remove dropped photos from the shop first, then release their storage objects.
        if (moderationShopDto.ShopPhotos is not null)
        {
            var keptStorageKeys = moderationShopDto.ShopPhotos
                .Select(p => p.StorageKey)
                .ToHashSet();
            var removedPhotos = shop.RemovePhotosExcept(keptStorageKeys);

            foreach (var removedPhoto in removedPhotos)
            {
                await bus.PublishAsync(new PhotoReplacedEvent(
                    removedPhoto.Id,
                    removedPhoto.StorageKey,
                    Guid.Empty,
                    "Shop",
                    shop.Id,
                    DateTime.UtcNow));
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        var result = mapper.Map<ModerationShopDto>(shop);

        return UpdateEntityResponse<ModerationShopDto>.Success(result, "Shop updated successfully");
    }
}
