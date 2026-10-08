namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Moderation-service operational insights for the admin dashboard.</summary>
/// <param name="Sla">Check-in moderation turnaround over the last 30 days.</param>
/// <param name="Queues">Always 5 entries in order: shops, checkIns, roasters, changeRequests, issueReports.</param>
/// <param name="OldestPendingHours">Age in hours (1 decimal) of the oldest pending item across all queues; null when all queues are empty.</param>
/// <param name="TopModerators30Days">Top 10 moderators by audit-log actions in the last 30 days.</param>
public record AdminModerationInsightsDto(
    AdminModerationSlaDto Sla,
    IReadOnlyList<AdminModerationQueueDto> Queues,
    double? OldestPendingHours,
    IReadOnlyList<AdminModeratorWorkloadDto> TopModerators30Days);
