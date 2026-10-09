using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Drinks;

namespace CoffeePeek.Moderation.Infrastructure.Consumers;

public static class CheckInModerationRequestedHandler
{
    public static async Task Handle(CheckInModerationRequestedEvent message,
        IModerationReviewRepository repository, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var existing = await repository.GetByCheckInRevision(message.CheckInId, message.ContentRevision, ct);
        if (existing is not null)
        {
            if (existing.UserId != message.UserId || existing.ShopId != message.ShopId)
                throw new DomainException("The check-in revision has conflicting ownership.");
            return;
        }

        if (message.Photos is null ||
            message.Photos.Select(p => p.Id).Distinct().Count() != message.Photos.Length ||
            message.Photos.Select(p => p.SortIndex).Distinct().Count() != message.Photos.Length ||
            message.Photos.Select(p => p.StorageKey).Distinct(StringComparer.Ordinal).Count() != message.Photos.Length)
            throw new DomainException("Invalid check-in snapshot content.");

        if (message.DrinkSlug is null && (message.CustomDrinkName is not null || message.DrinkNameRu is not null || message.DrinkNameEn is not null))
            throw new DomainException("A frozen drink requires its slug.");
        var drink = message.DrinkSlug is null ? null : new DrinkSelection(message.DrinkSlug,
            message.CustomDrinkName, message.DrinkNameRu!, message.DrinkNameEn!);
        var photos = message.Photos.OrderBy(p => p.SortIndex)
            .Select(p => PhotoMetadata.CreateCheckInPhoto(p.Id, p.SortIndex, p.FileName, p.ContentType,
                p.StorageKey, p.SizeBytes, message.UserId)).ToList();
        var review = ModerationReview.CreateCheckInSnapshot(message.CheckInId, message.ContentRevision,
            message.UserId, message.ShopId, message.Username, message.Text,
            message.Rating?.Place, message.Rating?.Service, message.Rating?.Coffee,
            drink, message.VisitedAtUtc, photos);
        repository.Add(review);
        // Existing xmin concurrency + durable EF outbox retry the entire handler after unique-key races.
        await unitOfWork.SaveChangesAsync(ct);
    }
}
