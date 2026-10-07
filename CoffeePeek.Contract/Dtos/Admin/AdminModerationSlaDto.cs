namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Check-in moderation SLA.</summary>
/// <param name="CheckInsModerated30Days">Check-in snapshots approved/rejected in the last 30 days (rolling).</param>
/// <param name="AvgCheckInModerationHours">Average hours from submission to decision (1 decimal); null when nothing was moderated.</param>
public record AdminModerationSlaDto(int CheckInsModerated30Days, double? AvgCheckInModerationHours);
