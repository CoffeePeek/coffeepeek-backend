using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Events.Shops;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class CheckInMapping
{
    public static UnifiedCheckInDto ToDto(DomainCheckIn c, string shopName = "", CheckInDrinkDto? drink = null, bool helpful = false) => new()
    {
        Id = c.Id, UserId = c.UserId, Username = c.Username, ShopId = c.ShopId, ShopName = shopName,
        Text = c.Note, Rating = c.Rating == null ? null : new RatingDto { Place = c.Rating.Place, Service = c.Rating.Service, Coffee = c.Rating.Coffee },
        VisitedAt = c.VisitedAt, CreatedAtUtc = c.CreatedAtUtc,
        Visibility = (Contract.Enums.CheckInVisibility)c.Visibility,
        ModerationState = (Contract.Enums.CheckInModerationState)c.ModerationState,
        ContentRevision = c.ContentRevision, RejectionReason = c.RejectionReason, Drink = drink ?? (c.DrinkSlug == null ? null : new CheckInDrinkDto(c.DrinkSlug, c.CustomDrinkName, c.DrinkNameRu!, c.DrinkNameEn!)),
        Photos = c.ShopPhotos.OrderBy(p => p.SortIndex).ThenBy(p => p.Id)
            .Select(p => new CheckInPhotoDto(p.Id, p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.SortIndex,
                $"/api/v1/check-ins/{c.Id}/photos/{p.Id}")).ToArray(),
        HelpfulCount = c.HelpfulCount, IsHelpfulByCurrentUser = helpful
    };

    public static CheckInModerationRequestedEvent ModerationRequest(DomainCheckIn c) => new(c.Id, c.ContentRevision,
        c.UserId, c.Username, c.ShopId, c.Note, ToDto(c).Rating, c.DrinkSlug, c.CustomDrinkName, c.DrinkNameRu, c.DrinkNameEn, c.VisitedAt, ToDto(c).Photos);

}
