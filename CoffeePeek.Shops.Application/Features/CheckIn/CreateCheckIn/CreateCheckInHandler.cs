using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Validation;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using MapsterMapper;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.CheckIn.CreateCheckIn;

using Review = Domain.Aggregates.ReviewAggregate.Review;

public static class CreateCheckInHandler
{
    public static async Task<Response<CreateCheckInResponse>> Handle(
        CreateCheckInCommand command,
        IQueryCheckInRepository queryCheckInRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IAsyncValidationStrategy<CreateCheckInCommand> validationStrategy,
        IMapper mapper,
        ICacheService cacheService,
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

        if (command.Photos is { Count: > 0 })
        {
            var photos = command.Photos.Select(x =>
                new ShopPhoto(x.FileName, x.ContentType, x.StorageKey, x.Size, command.UserId));
            checkIn.AddPhotos(photos);
        }

        checkIn.AssignRating(command.Rating!.Place, command.Rating.Service, command.Rating.Coffee);

        queryCheckInRepository.Add(checkIn);

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
                    Photos = command.Photos?.ToArray() ?? []
                }
            });
        }

        await unitOfWork.SaveChangesAsync(ct);
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        return Response<CreateCheckInResponse>.Success(new CreateCheckInResponse(checkIn.Id));
    }
}
