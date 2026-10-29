namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

/// <summary>Raw (unrounded) shops insights aggregates.</summary>
public record AdminShopsInsights(
    AdminRatingAggregate Ratings,
    IReadOnlyList<AdminTopShop> TopShopsByCheckIns,
    AdminDownloadAggregate Downloads,
    AdminShopCoverage? Coverage = null);
