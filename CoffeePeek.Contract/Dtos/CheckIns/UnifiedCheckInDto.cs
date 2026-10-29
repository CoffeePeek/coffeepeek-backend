using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CheckIns;

public record UnifiedCheckInDto
{
    public CheckInKind Kind { get; init; }
    public HomeCheckInDetailsDto? Home { get; init; }
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Username { get; init; } = "";
    public Guid? ShopId { get; init; }
    public string ShopName { get; init; } = "";
    public string Text { get; init; }
    public RatingDto? Rating { get; init; }
    public DateTime VisitedAt { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public CheckInVisibility Visibility { get; init; }
    public CheckInModerationState ModerationState { get; init; }
    public int ContentRevision { get; init; }
    public string? RejectionReason { get; init; }
    public CheckInDrinkDto? Drink { get; init; }
    public CheckInPhotoDto? DrinkPhoto { get; init; }
    /// <summary>Copy of the shelf-bag card shown with a home check-in.</summary>
    public CheckInPhotoDto? ShelfBagPhoto { get; init; }
    public CheckInPhotoDto[] Photos { get; init; } = [];
    public int HelpfulCount { get; init; }
    public bool IsHelpfulByCurrentUser { get; init; }
    public bool CountsForLeaderboard { get; init; }
    public string LeaderboardStatus { get; init; } = "MissingLocation";
}
