namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record HomeCheckInDetailsDto
{
    public DateOnly PreparedOn { get; init; }
    public string? BrewMethodCode { get; init; }
    public Guid? CoffeeProductId { get; init; }
    /// <summary>Current public slug while the product is published; resolved on read, never stored.</summary>
    public string? CoffeeProductSlug { get; init; }
    public string? BeanName { get; init; }
    public string? RoasterName { get; init; }
    public decimal? DoseGrams { get; init; }
    public int? BrewDurationSeconds { get; init; }
    public int? TasteScore { get; init; }
}
