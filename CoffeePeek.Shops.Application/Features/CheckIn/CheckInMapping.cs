using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Events.Shops;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class CheckInMapping
{
    public static UnifiedCheckInDto ToDto(DomainCheckIn c, string shopName = "", CheckInDrinkDto? drink = null, bool helpful = false) => new()
    {
        Id = c.Id, UserId = c.UserId, Username = c.Username, ShopId = c.ShopId ?? Guid.Empty, ShopName = shopName,
        Text = c.Note, Rating = c.Rating == null ? null : new RatingDto { Place = c.Rating.Place, Service = c.Rating.Service, Coffee = c.Rating.Coffee },
        VisitedAt = c.VisitedAt, CreatedAtUtc = c.CreatedAtUtc,
        Visibility = (Contract.Enums.CheckInVisibility)c.Visibility,
        ModerationState = (Contract.Enums.CheckInModerationState)c.ModerationState,
        ContentRevision = c.ContentRevision, RejectionReason = c.RejectionReason, Drink = drink ?? (c.DrinkSlug == null ? null : new CheckInDrinkDto(c.DrinkSlug, c.CustomDrinkName, c.DrinkNameRu!, c.DrinkNameEn!)),
        Photos = c.ShopPhotos.Where(p => p.Id != c.DrinkPhotoId).OrderBy(p => p.SortIndex).ThenBy(p => p.Id)
            .Select(p => Photo(c.Id, p)).ToArray(),
        DrinkPhoto = c.ShopPhotos.Where(p => p.Id == c.DrinkPhotoId).Select(p => Photo(c.Id, p)).SingleOrDefault(),
        HelpfulCount = c.HelpfulCount, IsHelpfulByCurrentUser = helpful
    };

    public static CheckInModerationRequestedEvent ModerationRequest(DomainCheckIn c)
    {
        if (c.Kind != CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckInKind.Shop || c.ShopId == null)
            throw new InvalidOperationException("Only shop check-ins can be submitted for moderation.");
        return new CheckInModerationRequestedEvent(c.Id, c.ContentRevision,
            c.UserId, c.Username, c.ShopId.Value, c.Note, ToDto(c).Rating, c.DrinkSlug, c.CustomDrinkName,
            c.DrinkNameRu, c.DrinkNameEn, c.VisitedAt,
            c.ShopPhotos.OrderBy(p => p.SortIndex).Select(p => Photo(c.Id, p)).ToArray());
    }

    private static CheckInPhotoDto Photo(Guid checkInId, CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckInPhoto p) =>
        new(p.Id, p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.SortIndex,
            $"/api/v1/check-ins/{checkInId}/photos/{p.Id}");

}
