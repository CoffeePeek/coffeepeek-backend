using CoffeePeek.Moderation.Application.Common.Models;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using DomainCoffeeFocus = CoffeePeek.Moderation.Domain.Aggregates.Enums.CoffeeFocus;
using DomainPriceRange = CoffeePeek.Moderation.Domain.Aggregates.Enums.PriceRange;
using ModerationShop = CoffeePeek.Moderation.Domain.Aggregates.ModerationShop;

namespace CoffeePeek.Moderation.Application.Features.Shop.CreateShop;

public class ModerationShopCreationService(IModerationShopRepository shopRepository, IUnitOfWork unitOfWork)
    : IModerationShopCreationService
{
    public async Task<Guid> Create(
        SendCoffeeShopToModerationCommand command,
        GeocodingResult? geocodingResult,
        CancellationToken ct)
    {
        var shop = ModerationShop.Create(
            command.Name,
            command.UserId,
            command.CityId,
            command.Description,
            command.Type is null ? null : (DomainCoffeeFocus)command.Type.Value
        );

        var location = geocodingResult != null
            ? new ModerationLocation(command.Address, geocodingResult.Latitude, geocodingResult.Longitude)
            : new ModerationLocation(command.Address);
        shop.SetLocation(location);

        if (command.PriceRange != null)
        {
            shop.AddPriceRange((DomainPriceRange)command.PriceRange.Value);
        }

        if (command.ShopContact != null)
        {
            shop.UpdateContacts(
                command.ShopContact.PhoneNumber,
                command.ShopContact.InstagramLink,
                command.ShopContact.Email,
                command.ShopContact.SiteLink);
        }

        if (command.Schedules != null)
        {
            var schedules = command.Schedules.Select(s =>
                (s.DayOfWeek, s.IsClosed,
                    s.IsClosed
                        ? new List<(TimeSpan OpenTime, TimeSpan CloseTime)>()
                        : (s.Intervals ?? []).Select(i => (i.OpenTime, i.CloseTime)).ToList()));
            shop.UpdateSchedules(schedules);
        }

        shop.UpdateRelations(
            command.EquipmentIds ?? [],
            command.CoffeeBeanIds ?? [],
            command.RoasterIds ?? [],
            command.BrewMethodIds ?? []);

        if (command.ShopPhotos != null)
        {
            foreach (var photo in command.ShopPhotos)
            {
                shop.AddPhoto(photo.FileName, photo.ContentType, photo.StorageKey, photo.Size);
            }
        }

        if (command.MenuPhotos is { Count: > 0 })
        {
            shop.AttachMenuPhotos(
                command.MenuPhotos.Select(p => (p.FileName, p.ContentType, p.StorageKey, p.Size)).ToArray(),
                DateTime.UtcNow);
        }

        await shopRepository.AddAsync(shop);

        await unitOfWork.SaveChangesAsync(ct);
        
        return shop.Id;
    }
}
