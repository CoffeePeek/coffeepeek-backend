namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public interface IAdminUserQueryRepository
{
    Task<(IReadOnlyList<User> Items, int TotalCount)> GetUsersAsync(
        int page,
        int pageSize,
        string? search,
        string? role,
        CancellationToken ct = default);

    Task<AdminUserStats> GetStatsAsync(CancellationToken ct = default);

    /// <summary>New user registrations per UTC day for users created at or after <paramref name="fromUtcDate"/>.</summary>
    Task<IReadOnlyDictionary<DateOnly, int>> GetNewUsersPerDayAsync(DateTime fromUtcDate, CancellationToken ct = default);
}

public record AdminUserStats(
    int TotalUsers,
    int ActiveUsers,
    int BlockedUsers,
    int RegisteredToday,
    IReadOnlyDictionary<string, int> UsersByRole,
    int DeletedUsers = 0,
    int DailyActiveUsers = 0,
    int WeeklyActiveUsers = 0,
    int MonthlyActiveUsers = 0,
    int EmailConfirmedUsers = 0,
    int GoogleUsers = 0);
