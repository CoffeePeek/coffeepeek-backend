#nullable enable

namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record HomeCheckInDto
{
    public CoffeePeek.Contract.Enums.CheckInVisibility Visibility { get; init; }
    public CoffeePeek.Contract.Enums.CheckInModerationState ModerationState { get; init; }
    public int ContentRevision { get; init; }
    public Guid Id { get; init; }
    public DateTime PreparedAtUtc { get; init; }
    public DateOnly PreparedOn { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string Text { get; init; } = "";
    public string? BrewMethodCode { get; init; }
    public Guid? CoffeeProductId { get; init; }
    /// <summary>Historical name snapshot, or the author's custom name.</summary>
    public string? BeanName { get; init; }
    /// <summary>Roaster snapshot from the catalog product or the shelf bag.</summary>
    public string? RoasterName { get; init; }
    /// <summary>The author's shelf bag the bean came from; the bag may since have been deleted.</summary>
    public Guid? ShelfBagId { get; init; }
    public decimal? DoseGrams { get; init; }
    public int? BrewDurationSeconds { get; init; }
    public int? TasteScore { get; init; }
    public CheckInDrinkDto? Drink { get; init; }
    public CheckInPhotoDto[] Photos { get; init; } = [];
    public CheckInPhotoDto? DrinkPhoto { get; init; }
}
