using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
namespace CoffeePeek.Contract.Dtos.PublicAddresses;
public sealed record PublicCheckInDto
{
    public Guid Id { get; init; }
    public PublicAddressDto? Shop { get; init; }
    public PublicAddressDto? Author { get; init; }
    public string Username { get; init; } = "";
    public string ShopName { get; init; } = "";
    public string Text { get; init; } = "";
    public RatingDto? Rating { get; init; }
    public DateTime VisitedAt { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public CheckInVisibility Visibility { get; init; }
    public CheckInModerationState ModerationState { get; init; }
    public int ContentRevision { get; init; }
    public string? RejectionReason { get; init; }
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public string? DrinkNameRu { get; init; }
    public string? DrinkNameEn { get; init; }
    public string? DrinkPhotoUrl { get; init; }
    public CheckInPhotoDto[] Photos { get; init; } = [];
    public int HelpfulCount { get; init; }
    public bool IsHelpfulByCurrentUser { get; init; }
}
