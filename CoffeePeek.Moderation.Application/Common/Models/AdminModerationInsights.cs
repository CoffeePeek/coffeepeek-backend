namespace CoffeePeek.Moderation.Application.Common.Models;

/// <param name="CheckInsModerated">Check-ins approved/rejected in the window.</param>
/// <param name="AvgCheckInModerationHours">Unrounded mean submission-to-decision hours; null when nothing was moderated.</param>
/// <param name="Queues">Queues in fixed order: shops, checkIns, roasters, changeRequests, issueReports.</param>
/// <param name="Workload">Top moderators by action count in the window, descending.</param>
public record AdminModerationInsights(
    int CheckInsModerated,
    double? AvgCheckInModerationHours,
    IReadOnlyList<AdminModerationQueueSnapshot> Queues,
    IReadOnlyList<AdminModeratorWorkload> Workload);
