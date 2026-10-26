using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ModerationReviewDto
{
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public string? DrinkNameRu { get; init; }
    public string? DrinkNameEn { get; init; }

    public Guid Id { get; init; }

    public Guid CheckInId { get; init; }
    public int ContentRevision { get; init; }
    public string LeaderboardGeoStatus { get; init; } = "MissingLocation";
    public double? ShopDistanceMeters { get; init; }
    public double? LocationAccuracyMeters { get; init; }
    public DateTime? LocationCapturedAtUtc { get; init; }
    public bool? LeaderboardOverride { get; init; }
    public string? LeaderboardReason { get; init; }
    public DateTime VisitedAtUtc { get; init; }
    public string Text { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; }
    public CoffeePeek.Contract.Dtos.CheckIns.HomeCheckInDetailsDto? Home { get; init; }
    public CheckInKind Kind { get; init; }
    public Guid? ShopId { get;init; }

    public RatingDto? Rating { get; init; }

    public string? RejectedReason  { get; init; }
    public Guid? ModeratedBy  { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ModeratedAt  { get; init; }
    public ModerationStatus ModerationStatus { get; init; }
    public PhotoMetadataDto[] Photos { get; init; } = [];
}
