namespace CoffeePeek.Moderation.Domain.Aggregates;

public interface IAdminModerationStatsQueryRepository
{
    Task<(int PendingShops, int PendingCheckIns)> GetStatsAsync(CancellationToken ct = default);

    /// <summary>Raw moderation SLA, queue and workload aggregates; 30-day windows end at <paramref name="nowUtc"/>.</summary>
    Task<AdminModerationInsights> GetInsightsAsync(DateTime nowUtc, CancellationToken ct = default);
}
