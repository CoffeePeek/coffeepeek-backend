#nullable enable

namespace CoffeePeek.Contract.Dtos.CheckIns;

/// <summary>Complete replacement of editable home-brew fields. Omitted optional values are cleared.</summary>
public sealed record UpdateHomeCheckInRequest
{
    /// <summary>ISO 8601 timestamp with an explicit UTC offset or Z.</summary>
    public CoffeePeek.Contract.Enums.CheckInVisibility? Visibility { get; init; }
    public string? PreparedAt { get; init; }
    public DateOnly? PreparedOn { get; init; }
    public string? Text { get; init; }
    public string? BrewMethodCode { get; init; }
    /// <summary>Keep this entry's existing catalog product and historical name snapshot.</summary>
    public Guid? CoffeeProductId { get; init; }
    /// <summary>Select a new currently published catalog product by public slug.</summary>
    public string? CoffeeProductSlug { get; init; }
    public string? CustomBeanName { get; init; }
    /// <summary>The author's shelf bag. The entry's current bag keeps its snapshots even if the bag changed or was deleted; omitting it clears the link.</summary>
    public Guid? ShelfBagId { get; init; }
    public decimal? DoseGrams { get; init; }
    public int? BrewDurationSeconds { get; init; }
    public int? TasteScore { get; init; }
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
}
