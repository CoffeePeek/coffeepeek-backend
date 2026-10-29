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

    /// <summary>Distinct users with a login/refresh per UTC day at or after <paramref name="fromUtcDate"/>.</summary>
    Task<IReadOnlyDictionary<DateOnly, int>> GetDailyActiveUsersPerDayAsync(DateTime fromUtcDate, CancellationToken ct = default);
}
