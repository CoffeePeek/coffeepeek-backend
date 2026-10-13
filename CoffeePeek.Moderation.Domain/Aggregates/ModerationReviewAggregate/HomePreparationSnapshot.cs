namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public sealed record HomePreparationSnapshot
{
    public DateOnly PreparedOn { get; init; }
    public string? BrewMethodCode { get; init; }
    public Guid? CoffeeProductId { get; init; }
    public string? BeanName { get; init; }
    public decimal? DoseGrams { get; init; }
    public int? BrewDurationSeconds { get; init; }
    public int? TasteScore { get; init; }
}
