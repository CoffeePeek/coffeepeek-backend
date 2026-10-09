using CoffeePeek.Contract.Enums;
namespace CoffeePeek.Contract.Dtos.CheckIns;
public record CreateUnifiedCheckInRequest(Guid CoffeeShopId, string? Text = null, RatingDto? Rating = null,
    CheckInVisibility Visibility = CheckInVisibility.Private, string? DrinkSlug = null, string? CustomDrinkName = null,
    UploadedPhotoDto[]? Photos = null, DateTimeOffset? VisitedAt = null);
