#nullable enable

namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record HomeCheckInDto
{
    public Guid Id { get; init; }
    public DateTime PreparedAtUtc { get; init; }
    public DateOnly PreparedOn { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string Text { get; init; } = "";
    public string? BrewMethodCode { get; init; }
    public Guid? CoffeeProductId { get; init; }
    /// <summary>Historical name snapshot, or the author's custom name.</summary>
    public string? BeanName { get; init; }
    public decimal? DoseGrams { get; init; }
    public int? BrewDurationSeconds { get; init; }
    public int? TasteScore { get; init; }
    public CheckInDrinkDto? Drink { get; init; }
    public CheckInPhotoDto[] Photos { get; init; } = [];
    public CheckInPhotoDto? DrinkPhoto { get; init; }
}
