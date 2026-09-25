namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Moderation-service operational insights for the admin dashboard.</summary>
/// <param name="Sla">Review moderation turnaround over the last 30 days.</param>
/// <param name="Queues">Always 5 entries in order: shops, reviews, roasters, changeRequests, issueReports.</param>
/// <param name="OldestPendingHours">Age in hours (1 decimal) of the oldest pending item across all queues; null when all queues are empty.</param>
/// <param name="TopModerators30Days">Top 10 moderators by audit-log actions in the last 30 days.</param>
public record AdminModerationInsightsDto(
    AdminModerationSlaDto Sla,
    IReadOnlyList<AdminModerationQueueDto> Queues,
    double? OldestPendingHours,
    IReadOnlyList<AdminModeratorWorkloadDto> TopModerators30Days);

/// <summary>Review moderation SLA.</summary>
/// <param name="ReviewsModerated30Days">Reviews approved/rejected in the last 30 days (rolling).</param>
/// <param name="AvgReviewModerationHours">Average hours from submission to decision (1 decimal); null when nothing was moderated.</param>
public record AdminModerationSlaDto(int ReviewsModerated30Days, double? AvgReviewModerationHours);

/// <summary>A moderation queue's pending count and oldest pending item age in hours (null when empty).</summary>
public record AdminModerationQueueDto(string Queue, int Pending, double? OldestPendingHours);

/// <summary>Moderator audit-log action counts in the last 30 days.</summary>
public record AdminModeratorWorkloadDto(Guid ModeratorUserId, int Total, int Approved, int Rejected);
