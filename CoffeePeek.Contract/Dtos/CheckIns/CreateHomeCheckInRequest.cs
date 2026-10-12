#nullable enable

using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Contract.Dtos.CheckIns;

/// <summary>One preparation, regardless of how many servings it produced.</summary>
public sealed record CreateHomeCheckInRequest
{
    /// <summary>ISO 8601 timestamp with an explicit UTC offset or Z.</summary>
    public string? PreparedAt { get; init; }
    /// <summary>Preparation calendar date; defaults to the date in PreparedAt's offset.</summary>
    public DateOnly? PreparedOn { get; init; }
    public string? Text { get; init; }
    public string? BrewMethodCode { get; init; }
    /// <summary>Slug from the public coffee catalog; mutually exclusive with CustomBeanName.</summary>
    public string? CoffeeProductSlug { get; init; }
    public string? CustomBeanName { get; init; }
    /// <summary>Dry coffee used for the entire preparation, in grams.</summary>
    public decimal? DoseGrams { get; init; }
    /// <summary>Total preparation time, in seconds.</summary>
    public int? BrewDurationSeconds { get; init; }
    public int? TasteScore { get; init; }
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public UploadedPhotoDto[]? Photos { get; init; }
    public UploadedPhotoDto? DrinkPhoto { get; init; }
}
