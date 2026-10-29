using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Wolverine;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class CreateHomeCheckInHandler
{
    public static async Task<Response<HomeCheckInDto>> Handle(CreateHomeCheckInCommand command,
        IQueryCheckInRepository repository, CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog drinks, IPublicAddressService addresses,
        IUploadedPhotoLookup photos, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var request = command.Request;
        var (preparedAtUtc, preparedOn) = HomeCheckInInput.ParseTime(request.PreparedAt, request.PreparedOn);
        var bean = await HomeCheckInInput.ResolveBeanAsync(command.UserId,
            request.CoffeeProductSlug, request.CustomBeanName, request.ShelfBagId, repository, ct);
        var brewMethodCode = await HomeCheckInInput.ResolveBrewMethodAsync(request.BrewMethodCode, addresses, ct);
        var keys = request.Photos?.Select(p => p?.StorageKey ?? "").ToArray() ?? [];
        if (keys.Length > BusinessConstants.MaxCheckInPhotos || keys.Any(k => string.IsNullOrWhiteSpace(k)
            || k.Length > BusinessConstants.MaxCheckInPhotoStorageKeyLength)
            || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ValidationException("Photos must be distinct, valid storage keys; at most five photos are allowed.");
        var drinkKey = request.DrinkPhoto?.StorageKey;
        if (request.DrinkPhoto != null && (keys.Length == 0 || string.IsNullOrWhiteSpace(drinkKey)
            || drinkKey.Length > BusinessConstants.MaxCheckInPhotoStorageKeyLength || keys.Contains(drinkKey, StringComparer.Ordinal)))
            throw new ValidationException("DrinkPhoto requires an original photo and a separate valid storage key.");
        var allKeys = drinkKey == null ? keys : keys.Append(drinkKey).ToArray();

        var checkIn = DomainCheckIn.CreateHome(command.UserId, command.Username, request.Text, preparedAtUtc, preparedOn,
            brewMethodCode, bean.CoffeeProductId, bean.BeanName, request.DoseGrams, request.BrewDurationSeconds,
            request.TasteScore, CreateUnifiedCheckInHandler.CreateId(command.UserId, allKeys),
            (CheckInVisibility)request.Visibility);
        checkIn.SetBeanSource(bean.ShelfBagId, bean.RoasterName);
        var drink = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync(
            request.DrinkSlug, request.CustomDrinkName, drinks, ct);
        checkIn.SetDrink(drink);

        if (keys.Length > 0)
        {
            if (await repository.GetTrackedByIdAsync(checkIn.Id, ct) != null)
                throw new ConflictException("The uploads already belong to a committed check-in.");
            if (await repository.ArePhotoKeysUsedAsync(allKeys, ct))
                throw new ValidationException("One or more photos are already attached to content.");
            var claim = await photos.ClaimForCheckInAsync(command.UserId, checkIn.Id, allKeys, ct, drinkKey);
            if (!claim.Succeeded)
                throw new ValidationException("Photos could not be claimed, please retry with the same uploads.");
            var trusted = claim.Photos.ToDictionary(p => p.RequestedStorageKey, StringComparer.Ordinal);
            if (trusted.Count != allKeys.Length || allKeys.Any(k => !trusted.ContainsKey(k)))
                throw new ValidationException("One or more photos were not found, do not belong to you, or were already used.");
            checkIn.AddPhotos(keys.Select((k, i) => new CheckInPhoto(trusted[k].FileName, trusted[k].ContentType,
                trusted[k].StorageKey, trusted[k].SizeBytes, command.UserId, i, trusted[k].PhotoId)));
            if (drinkKey != null)
            {
                var cutout = trusted[drinkKey];
                checkIn.AddDrinkPhoto(new CheckInPhoto(cutout.FileName, cutout.ContentType, cutout.StorageKey,
                    cutout.SizeBytes, command.UserId, keys.Length, id: cutout.PhotoId));
            }
        }
        if (request.ShowShelfBag && bean.ShelfBagId is { } bagId && bean.CardPhotoId is { } cardPhotoId)
            await HomeCheckInBagCard.AttachAsync(checkIn, bagId, cardPhotoId, photos, ct);

        repository.Add(checkIn);
        await bus.PublishAsync(new CoffeePeek.Contract.Events.Shops.UserCheckInActivityRecorded(command.UserId, checkIn.CreatedAtUtc));
        if (checkIn.Visibility == CheckInVisibility.Public)
            await bus.PublishAsync(CheckInMapping.ModerationRequest(checkIn));
        if (checkIn.ShopPhotos.Count > 0)
            await bus.PublishAsync(new CheckInPhotosAttachedEvent(command.UserId, checkIn.Id,
                checkIn.ShopPhotos.OrderBy(p => p.SortIndex).Select(p => p.StorageKey).ToArray(), DateTime.UtcNow));
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id, null, checkIn.UserId));
        await unitOfWork.SaveChangesAsync(ct);
        return Response<HomeCheckInDto>.Success(HomeCheckInMapping.ToDto(checkIn));
    }
}
