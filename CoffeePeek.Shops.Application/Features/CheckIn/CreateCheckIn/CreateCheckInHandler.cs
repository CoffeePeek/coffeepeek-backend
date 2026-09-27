using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Validation;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using MapsterMapper;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.CheckIn.CreateCheckIn;

using Review = Domain.Aggregates.ReviewAggregate.Review;

public static class CreateCheckInHandler
{
    public const string PhotosNotVerifiedMessage = "Photos could not be verified, please try again";
    public const string PhotosNotFoundMessage =
        "One or more photos were not found, do not belong to you, or were already used";

    public static async Task<Response<CreateCheckInResponse>> Handle(
        CreateCheckInCommand command,
        IQueryCheckInRepository queryCheckInRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IAsyncValidationStrategy<CreateCheckInCommand> validationStrategy,
        IMapper mapper,
        ICacheService cacheService,
        IUploadedPhotoLookup photoLookup,
        CancellationToken ct)
    {
        // Cooldown and daily cap are read-then-insert checks; without the lock parallel requests all pass.
        await queryCheckInRepository.LockUserForCheckInAsync(command.UserId, ct);

        var validationResult = await validationStrategy.ValidateAsync(command, ct);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.ErrorMessage!);

        var checkIn = Domain.Aggregates.CheckInAggregate.CheckIn.Create(
            command.UserId,
            command.CoffeeShopId,
            command.VisitedAt);

        if (!string.IsNullOrEmpty(command.Note))
            checkIn.UpdateNote(command.Note);

        // Only the storage key from the client is trusted. Ownership, single use and the real
        // FileName/ContentType/Size come from MediaService; fail closed if it cannot be asked.
        var storageKeys = command.Photos?.Select(p => p.StorageKey).ToArray() ?? [];
        UploadedPhotoDto[] trustedPhotos = [];
        if (storageKeys.Length > 0)
        {
            var lookup = await photoLookup.ResolveAsync(command.UserId, storageKeys, ct);
            if (!lookup.Succeeded)
            {
                if (lookup.IsTransientFailure)
                    throw new ServiceUnavailableException(PhotosNotVerifiedMessage);
                throw new ValidationException(PhotosNotVerifiedMessage);
            }

            var resolved = lookup.Photos.ToDictionary(p => p.StorageKey, StringComparer.Ordinal);
            if (storageKeys.Any(k => !resolved.ContainsKey(k)))
                throw new ValidationException(PhotosNotFoundMessage);

            trustedPhotos = storageKeys
                .Select(k => resolved[k])
                .Select(r => new UploadedPhotoDto(r.FileName, r.ContentType, r.StorageKey, r.SizeBytes))
                .ToArray();

            checkIn.AddPhotos(trustedPhotos.Select(p =>
                new ShopPhoto(p.FileName, p.ContentType, p.StorageKey, p.Size, command.UserId)));
        }

        checkIn.AssignRating(command.Rating!.Place, command.Rating.Service, command.Rating.Coffee);

        queryCheckInRepository.Add(checkIn);

        if (storageKeys.Length > 0)
        {
            // Durable outbox: only shipped once SaveChangesAsync commits the check-in.
            await bus.PublishAsync(new CheckInPhotosAttachedEvent(
                command.UserId, checkIn.Id, storageKeys, DateTime.UtcNow));
        }

        if (command.IsPublic)
        {
            // Transient review used only to shape the ReviewDto for the CheckinCreatedEvent.
            // It is never persisted and is not yet linked to any moderation review, so pass
            // Guid.Empty and null out the DTO link below (the real link is set on approval).
            var review = Review.Create(
                command.CoffeeShopId,
                command.UserId,
                command.UserName,
                header: string.IsNullOrWhiteSpace(command.Header) ? null : command.Header.Trim(),
                comment: command.Note!.Trim(),
                ratingPlace: command.Rating.Place,
                ratingService: command.Rating.Service,
                ratingCoffee: command.Rating.Coffee,
                moderationReviewId: Guid.Empty);

            await bus.PublishAsync(new CheckinCreatedEvent
            {
                UserId = command.UserId,
                ShopId = command.CoffeeShopId,
                CreatedAt = checkIn.CreatedAtUtc,
                ReviewDto = mapper.Map<ReviewDto>(review) with
                {
                    Username = command.UserName,
                    ModerationReviewId = null,
                    Photos = trustedPhotos
                }
            });
        }

        await unitOfWork.SaveChangesAsync(ct);
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        return Response<CreateCheckInResponse>.Success(new CreateCheckInResponse(checkIn.Id));
    }
}
