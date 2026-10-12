namespace CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;

public sealed class MenuDraftUnmatched
{
    public string RawName { get; init; } = "";
    public decimal? Price { get; init; }
    public double? Confidence { get; init; }
}
