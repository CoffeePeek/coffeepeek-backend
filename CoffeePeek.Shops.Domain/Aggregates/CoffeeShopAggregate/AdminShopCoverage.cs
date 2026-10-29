namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

/// <summary>All catalog shops, including hidden and closed; coverage uses current public approved shop check-ins.</summary>
public record AdminShopCoverage(
    int TotalShops,
    decimal? AverageCompletenessPercent,
    int ShopsWithCheckIns,
    int ShopsWithoutCheckIns);
