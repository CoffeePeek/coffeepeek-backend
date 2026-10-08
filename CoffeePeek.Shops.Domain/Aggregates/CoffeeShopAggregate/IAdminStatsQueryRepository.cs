namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IAdminStatsQueryRepository
{
    Task<AdminShopsStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>Ratings, top shops and download aggregates; "recent" windows start at <paramref name="sinceUtc"/>.</summary>
    Task<AdminShopsInsights> GetInsightsAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Per-UTC-day counts of new shops, non-deleted reviews and check-ins created at or after <paramref name="fromUtcDate"/>.</summary>
    Task<AdminShopsDailyCounts> GetDailyCountsAsync(DateTime fromUtcDate, CancellationToken cancellationToken = default);
}
