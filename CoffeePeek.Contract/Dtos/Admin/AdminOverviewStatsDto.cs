namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>
/// Platform admin dashboard totals. Missing downstream services return 0
/// and flip the matching <c>*Available</c> flag — the endpoint always succeeds.
/// </summary>
/// <param name="ActiveUsers">Users that are neither blocked nor soft-deleted.</param>
/// <param name="BlockedUsers">Blocked users that are not soft-deleted.</param>
/// <param name="DailyActiveUsers">Distinct users with a login/refresh (refresh token issued) in the last 24 hours.</param>
/// <param name="WeeklyActiveUsers">Distinct users with a login/refresh in the last 7 days (rolling).</param>
/// <param name="MonthlyActiveUsers">Distinct users with a login/refresh in the last 30 days (rolling).</param>
/// <param name="DeletedUsers">Soft-deleted users.</param>
/// <param name="EmailConfirmedUsers">Users whose email is confirmed (external OAuth users count as confirmed).</param>
/// <param name="GoogleUsers">Users registered through an external OAuth provider (Google).</param>
public record AdminOverviewStatsDto(
    int TotalUsers,
    int UsersRegisteredToday,
    int ActiveUsers,
    int BlockedUsers,
    int TotalCoffeeShops,
    int TotalCheckIns,
    int PendingModerationShops,
    int PendingModerationCheckIns,
    int NewCoffeeShopsToday,
    int NewCheckInsToday,
    AdminImportOverviewStatsDto Import,
    bool ShopsAvailable = true,
    bool ModerationAvailable = true,
    int DailyActiveUsers = 0,
    int WeeklyActiveUsers = 0,
    int MonthlyActiveUsers = 0,
    int DeletedUsers = 0,
    int EmailConfirmedUsers = 0,
    int GoogleUsers = 0);
