using System.Net;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Domain.Entities;
using Wolverine;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Validation;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Review.UpdateCoffeeShopReview;

public static class UpdateCoffeeShopReviewHandler
{
    public static async Task<(Response<UpdateCoffeeShopReviewResponse>, ModerationReviewEditedEvent?)> Handle(
        UpdateCoffeeShopReviewCommand command,
        IValidationStrategy<UpdateCoffeeShopReviewCommand> validationStrategy,
        IModerationReviewRepository reviewRepository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog drinkCatalog,
        IReviewPhotoLookup photoLookup,
        IMessageBus bus,
        CancellationToken ct)
    {
        var validationResult = validationStrategy.Validate(command);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.ErrorMessage!);

        var review = await reviewRepository.GetById(command.ReviewId, ct);

        if (review == null)
        {
            return (Response<UpdateCoffeeShopReviewResponse>.Error(
                HttpStatusCode.NotFound, "Review not found"), null);
        }

        if (review.UserId != command.UserId)
        {
            return (Response<UpdateCoffeeShopReviewResponse>.Error(
                HttpStatusCode.Forbidden, "You are not authorized to update this review"), null);
        }

        // Verify every supplied new key before changing any tracked state.
        List<PhotoMetadata>? replacement = null;
        string[] newKeys = [];
        if (command.Photos is not null)
        {
            var retained = review.ReviewPhotos.ToDictionary(p => p.StorageKey, StringComparer.Ordinal);
            newKeys = command.Photos.Select(p => p.StorageKey).Where(k => !retained.ContainsKey(k)).ToArray();
            var resolved = new Dictionary<string, CoffeePeek.Contract.Dtos.ResolvedUploadedPhotoDto>(StringComparer.Ordinal);
            if (newKeys.Length > 0)
            {
                var lookup = await photoLookup.ResolveAsync(review.UserId, newKeys, ct);
                if (!lookup.Succeeded)
                {
                    if (lookup.IsTransientFailure)
                        throw new ServiceUnavailableException("Photos could not be verified, please try again.");
                    throw new ValidationException("Photos could not be verified.");
                }
                foreach (var photo in lookup.Photos)
                {
                    if (photo is null || !newKeys.Contains(photo.StorageKey, StringComparer.Ordinal) ||
                        string.IsNullOrWhiteSpace(photo.FileName) || string.IsNullOrWhiteSpace(photo.ContentType) ||
                        photo.SizeBytes <= 0 || !resolved.TryAdd(photo.StorageKey, photo))
                        throw new ValidationException("Media returned invalid photo metadata.");
                }
                if (newKeys.Any(k => !resolved.ContainsKey(k)))
                    throw new ValidationException("One or more photos were not found, do not belong to you, or were already used.");
            }

            replacement = command.Photos.Select(p => retained.TryGetValue(p.StorageKey, out var existing)
                ? existing
                : PhotoMetadata.Create(resolved[p.StorageKey].FileName, resolved[p.StorageKey].ContentType,
                    p.StorageKey, resolved[p.StorageKey].SizeBytes, review.UserId, moderationShopId: null)).ToList();
            if (replacement.Any(p => p.OwnerId != review.UserId || p.ModerationShopId is not null))
                throw new ValidationException("Review photos must belong to its author.");
        }

        var changeDrink = false;
        CoffeePeek.Shared.Kernel.Drinks.DrinkSelection? drink = null;
        if (command.ClearDrink)
        {
            if (command.DrinkSlug is not null || command.CustomDrinkName is not null)
                throw new ValidationException("ClearDrink cannot be combined with a drink selection.");
            changeDrink = true;
        }
        else if (command.DrinkSlug is not null || command.CustomDrinkName is not null)
        {
            // An unchanged choice remains valid after catalog deactivation or renaming.
            if (command.DrinkSlug?.Trim().ToLowerInvariant() != review.DrinkSlug ||
                (string.IsNullOrWhiteSpace(command.CustomDrinkName) ? null : command.CustomDrinkName.Trim()) != review.CustomDrinkName)
            {
                drink = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync(
                    command.DrinkSlug, command.CustomDrinkName, drinkCatalog, ct);
                changeDrink = true;
            }
        }

        if (changeDrink) review.SetDrink(drink);
        if (replacement is not null)
        {
            review.ReplacePhotos(replacement);
            reviewRepository.AddPhotos(replacement.Where(p => newKeys.Contains(p.StorageKey, StringComparer.Ordinal)));
        }

        review.UpdateHeader(command.Header);
        review.UpdateComment(command.Comment);
        review.Rating.UpdateRating(
            command.Rating.Place,
            command.Rating.Service,
            command.Rating.Coffee);

        // Editing sends the review back through moderation; the published shops review is
        // hidden by the Shops-side ModerationReviewEditedHandler until a moderator re-approves.
        review.Resubmit(command.UserId);

        if (newKeys.Length > 0)
            await bus.PublishAsync(new ReviewPhotosAttachedEvent(review.UserId, review.Id, newKeys, DateTime.UtcNow));

        await unitOfWork.SaveChangesAsync(ct);

        var dto = mapper.Map<ModerationReviewDto>(review);

        return (Response<UpdateCoffeeShopReviewResponse>.Success(new UpdateCoffeeShopReviewResponse(review.Id)),
            new ModerationReviewEditedEvent(dto));
    }
}