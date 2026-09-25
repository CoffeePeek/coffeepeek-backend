namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public record AdminShopsStats(
    int TotalCoffeeShops,
    int NewCoffeeShopsToday,
    int TotalReviews,
    int NewReviewsToday);

/// <summary>Per-UTC-day creation counts (days with no items are absent).</summary>
public record AdminShopsDailyCounts(
    IReadOnlyDictionary<DateOnly, int> Shops,
    IReadOnlyDictionary<DateOnly, int> Reviews,
    IReadOnlyDictionary<DateOnly, int> CheckIns);

/// <summary>Raw (unrounded) shops insights aggregates.</summary>
public record AdminShopsInsights(
    AdminRatingAggregate Ratings,
    IReadOnlyList<AdminTopShop> TopShopsByCheckIns,
    IReadOnlyList<AdminTopShop> TopShopsByReviews,
    AdminDownloadAggregate Downloads);

/// <param name="StarCounts">Review count per star (1..5), always 5 entries.</param>
public record AdminRatingAggregate(
    int TotalReviews,
    decimal AverageRating,
    decimal AveragePlace,
    decimal AverageService,
    decimal AverageCoffee,
    IReadOnlyList<(int Stars, int Count)> StarCounts);

public record AdminTopShop(Guid ShopId, string Name, int Count);

public record AdminDownloadAggregate(
    int Total,
    int SinceCount,
    IReadOnlyList<(string Channel, int Total, int SinceCount)> ByChannel,
    IReadOnlyList<(string Country, int Count)> TopCountriesSince);

public interface IAdminStatsQueryRepository
{
    Task<AdminShopsStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>Ratings, top shops and download aggregates; "recent" windows start at <paramref name="sinceUtc"/>.</summary>
    Task<AdminShopsInsights> GetInsightsAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Per-UTC-day counts of new shops, non-deleted reviews and check-ins created at or after <paramref name="fromUtcDate"/>.</summary>
    Task<AdminShopsDailyCounts> GetDailyCountsAsync(DateTime fromUtcDate, CancellationToken cancellationToken = default);
}
