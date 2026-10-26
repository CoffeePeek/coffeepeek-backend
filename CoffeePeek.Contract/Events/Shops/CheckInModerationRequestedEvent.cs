using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
namespace CoffeePeek.Contract.Events.Shops;
public record CheckInModerationRequestedEvent(Guid CheckInId, int ContentRevision, Guid UserId,
    string Username, Guid? ShopId, string Text, RatingDto? Rating, string? DrinkSlug, string? CustomDrinkName,
    string? DrinkNameRu, string? DrinkNameEn, DateTime VisitedAtUtc, CheckInPhotoDto[] Photos, HomeCheckInDetailsDto? Home = null,
    string LeaderboardGeoStatus = "MissingLocation", double? ShopDistanceMeters = null,
    double? LocationAccuracyMeters = null, DateTime? LocationCapturedAtUtc = null);
