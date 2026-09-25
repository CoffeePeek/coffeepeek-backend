namespace CoffeePeek.Moderation.Domain.Aggregates;

public interface IAdminModerationStatsQueryRepository
{
    Task<(int PendingShops, int PendingReviews)> GetStatsAsync(CancellationToken ct = default);

    /// <summary>Raw moderation SLA, queue and workload aggregates; 30-day windows end at <paramref name="nowUtc"/>.</summary>
    Task<AdminModerationInsights> GetInsightsAsync(DateTime nowUtc, CancellationToken ct = default);
}

/// <param name="ReviewsModerated">Reviews approved/rejected in the window.</param>
/// <param name="AvgReviewModerationHours">Unrounded mean submission-to-decision hours; null when nothing was moderated.</param>
/// <param name="Queues">Queues in fixed order: shops, reviews, roasters, changeRequests, issueReports.</param>
/// <param name="Workload">Top moderators by action count in the window, descending.</param>
public record AdminModerationInsights(
    int ReviewsModerated,
    double? AvgReviewModerationHours,
    IReadOnlyList<AdminModerationQueueSnapshot> Queues,
    IReadOnlyList<AdminModeratorWorkload> Workload);

public record AdminModerationQueueSnapshot(string Queue, int Pending, DateTime? OldestPendingCreatedAtUtc);

public record AdminModeratorWorkload(Guid ModeratorUserId, int Total, int Approved, int Rejected);
