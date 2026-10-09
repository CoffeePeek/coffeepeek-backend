using CoffeePeek.Contract.Dtos.CheckIns;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Wolverine;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class CreateUnifiedCheckInHandler
{
    public static async Task<Response<UnifiedCheckInDto>> Handle(CreateUnifiedCheckInCommand command,
        IQueryCheckInRepository repository, IQueryCoffeeShopRepository shops, CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog drinks,
        IUploadedPhotoLookup photos, IUnitOfWork unitOfWork, IMessageBus bus, ICacheService cache, CancellationToken ct)
    {
        var request = command.Request;
        if (request.VisitedAt == null) throw new ValidationException("VisitedAt is required.");
        var keys = request.Photos?.Select(p => p?.StorageKey ?? "").ToArray() ?? [];
        if (keys.Length > BusinessConstants.MaxCheckInPhotos || keys.Any(k => string.IsNullOrWhiteSpace(k)
            || k.Length > BusinessConstants.MaxCheckInPhotoStorageKeyLength)
            || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ValidationException("Photos must be distinct, valid storage keys; at most five photos are allowed.");
        var checkIn = DomainCheckIn.CreateUnified(command.UserId, request.CoffeeShopId, command.Username,
            request.Text, request.Rating == null ? null : Rating.Create(request.Rating.Place, request.Rating.Service, request.Rating.Coffee),
            request.VisitedAt.Value.UtcDateTime,
            (CheckInVisibility)request.Visibility, CreateId(command.UserId, keys));
        if (!await shops.Exists(request.CoffeeShopId, ct)) throw new NotFoundException("Coffee shop not found.");
        var drink = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync(request.DrinkSlug, request.CustomDrinkName, drinks, ct);
        checkIn.SetDrink(drink);
        await repository.LockUserForCheckInAsync(command.UserId, ct);
        var now = DateTime.UtcNow;
        if (await repository.ExistsSinceAsync(command.UserId, now.AddHours(-BusinessConstants.MinHoursBetweenUserCheckIns), ct))
            throw new ValidationException($"Check-ins can be created once every {BusinessConstants.MinHoursBetweenUserCheckIns} hours.");
        if (await repository.CountSinceAsync(command.UserId, now.Date, ct) >= BusinessConstants.MaxUserCheckInsPerDay)
            throw new ValidationException($"Check-ins are limited to {BusinessConstants.MaxUserCheckInsPerDay} per day.");
        if (keys.Length > 0)
        {
            if (await repository.GetTrackedByIdAsync(checkIn.Id, ct) != null)
                throw new ConflictException("The uploads already belong to a committed check-in.");
            if (await repository.ArePhotoKeysUsedAsync(keys, ct))
                throw new ValidationException("One or more photos are already attached to content.");
            var claim = await photos.ClaimForCheckInAsync(command.UserId, checkIn.Id, keys, ct);
            if (!claim.Succeeded) throw new ValidationException("Photos could not be claimed, please retry with the same uploads.");
            var trusted = claim.Photos.ToDictionary(p => p.RequestedStorageKey, StringComparer.Ordinal);
            if (trusted.Count != keys.Length || keys.Any(k => !trusted.ContainsKey(k)))
            {
                throw new ValidationException("One or more photos were not found, do not belong to you, or were already used.");
            }
            checkIn.AddPhotos(keys.Select((k, i) => new ShopPhoto(trusted[k].FileName, trusted[k].ContentType,
                trusted[k].StorageKey, trusted[k].SizeBytes, command.UserId, i, trusted[k].PhotoId)));
        }
        repository.Add(checkIn);
        if (keys.Length > 0) await bus.PublishAsync(new CheckInPhotosAttachedEvent(command.UserId, checkIn.Id,
            checkIn.ShopPhotos.OrderBy(p => p.SortIndex).Select(p => p.StorageKey).ToArray(), now));
        if (checkIn.Visibility == CheckInVisibility.Public) await bus.PublishAsync(CheckInMapping.ModerationRequest(checkIn));
        await bus.PublishAsync(new CoffeePeek.Contract.Events.Shops.CheckinCreatedEvent
            { UserId = command.UserId, ShopId = checkIn.ShopId, CheckInId = checkIn.Id, CreatedAt = checkIn.CreatedAtUtc });
        // A failed/ambiguous commit must retain reservations: freeing them could attach committed bytes elsewhere.
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id));
        await unitOfWork.SaveChangesAsync(ct);
        var names = await shops.GetShopNamesByIdsAsync([checkIn.ShopId], ct);
        return Response<UnifiedCheckInDto>.Success(CheckInMapping.ToDto(checkIn, names.GetValueOrDefault(checkIn.ShopId) ?? ""));
    }

    // Uploads are single use. A stable identity lets a retry recover a remote reservation whose reply was lost.
    internal static Guid CreateId(Guid userId, string[] keys) => keys.Length == 0 ? Guid.NewGuid()
        : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("check-in-upload:" + userId.ToString("N") + ":"
            + JsonSerializer.Serialize(keys.OrderBy(k => k, StringComparer.Ordinal)))).AsSpan(0, 16));
}
