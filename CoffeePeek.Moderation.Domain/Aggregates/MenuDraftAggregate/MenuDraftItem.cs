namespace CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;

public sealed class MenuDraftItem
{
    public string Slug { get; init; } = "";
    public string NameRu { get; init; } = "";
    public string NameEn { get; init; } = "";
    public int Category { get; init; }
    public int Availability { get; init; }
    public decimal? Price { get; init; }
    public int? VolumeMl { get; init; }
    public int Source { get; init; }
}
