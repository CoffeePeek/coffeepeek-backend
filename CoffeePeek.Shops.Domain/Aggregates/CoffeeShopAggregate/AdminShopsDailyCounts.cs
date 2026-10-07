namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

/// <summary>Per-UTC-day creation counts (days with no items are absent).</summary>
public record AdminShopsDailyCounts(
    IReadOnlyDictionary<DateOnly, int> Shops,
    IReadOnlyDictionary<DateOnly, int> CheckIns);
