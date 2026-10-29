using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Repositories;

public class AdminUserQueryRepository(AccountDbContext dbContext) : IAdminUserQueryRepository
{
    public async Task<(IReadOnlyList<User> Items, int TotalCount)> GetUsersAsync(
        int page,
        int pageSize,
        string? search,
        string? role,
        CancellationToken ct = default)
    {
        var query = BuildUsersQuery(search, role);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue))
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    internal IQueryable<User> BuildUsersQuery(string? search, string? role)
    {
        IQueryable<User> query = dbContext.Users;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            // Username and Email are value-converted properties. EF cannot translate their .Value
            // members in a predicate, so filter their mapped columns before composing the EF query.
            // strpos keeps % and _ literal, matching the previous Contains semantics.
            query = dbContext.Users.FromSqlInterpolated($"""
                SELECT * FROM "Users"
                WHERE strpos(lower("Username"), {term}) > 0
                   OR strpos(lower("Email"), {term}) > 0
                """);
        }

        query = query
            .AsNoTracking()
            .Include(u => u.Roles)
            .Include(u => u.PhotoMetadata);

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.Roles.Any(r => r.Name == role));
        }

        return query;
    }

    public async Task<AdminUserStats> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Sequential on purpose: a single scoped DbContext does not support concurrent queries.
        // One aggregate keeps provider buckets consistent even if a registration happens during this request.
        var authProviders = await dbContext.Users.AsNoTracking()
            .GroupBy(u => u.Credentials.OAuthProvider == null ? "" : u.Credentials.OAuthProvider.ToLower())
            .Select(g => new { Provider = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Provider, x => x.Count, ct);
        var totalUsers = authProviders.Values.Sum();
        var blockedUsers = await dbContext.Users.CountAsync(u => u.IsBlocked && !u.IsSoftDelete, ct);
        var deletedUsers = await dbContext.Users.CountAsync(u => u.IsSoftDelete, ct);
        var activeUsers = await dbContext.Users.CountAsync(u => !u.IsBlocked && !u.IsSoftDelete, ct);
        var registeredToday = await dbContext.Users.CountAsync(u => u.CreatedAtUtc >= today, ct);
        var emailConfirmedUsers = await dbContext.Users.CountAsync(u => u.Credentials.EmailConfirmed, ct);
        var googleUsers = authProviders.GetValueOrDefault("google");
        var appleUsers = authProviders.GetValueOrDefault("apple");
        var emailUsers = authProviders.GetValueOrDefault("");
        var retention = await GetRetentionAsync(today, ct);

        // A refresh token is issued on every login and rotation, so its creation time is the activity signal.
        var dailyActiveUsers = await CountActiveUsersSinceAsync(now.AddDays(-1), ct);
        var weeklyActiveUsers = await CountActiveUsersSinceAsync(now.AddDays(-7), ct);
        var monthlyActiveUsers = await CountActiveUsersSinceAsync(now.AddDays(-30), ct);

        var usersByRole = await dbContext.Users
            .AsNoTracking()
            .SelectMany(u => u.Roles)
            .GroupBy(r => r.Name)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Role, x => x.Count, ct);

        return new AdminUserStats(
            TotalUsers: totalUsers,
            ActiveUsers: activeUsers,
            BlockedUsers: blockedUsers,
            RegisteredToday: registeredToday,
            UsersByRole: usersByRole,
            DeletedUsers: deletedUsers,
            DailyActiveUsers: dailyActiveUsers,
            WeeklyActiveUsers: weeklyActiveUsers,
            MonthlyActiveUsers: monthlyActiveUsers,
            EmailConfirmedUsers: emailConfirmedUsers,
            GoogleUsers: googleUsers,
            AppleUsers: appleUsers,
            EmailUsers: emailUsers,
            OtherAuthUsers: totalUsers - googleUsers - appleUsers - emailUsers,
            Retention: retention);
    }

    private async Task<AdminUserRetentionStats> GetRetentionAsync(DateTime todayUtc, CancellationToken ct)
    {
        var d1 = await GetDayRetentionAsync(1, todayUtc, ct);
        var d7 = await GetDayRetentionAsync(7, todayUtc, ct);
        var d30 = await GetDayRetentionAsync(30, todayUtc, ct);
        var eligible = dbContext.Users.AsNoTracking().Where(u => u.CreatedAtUtc < todayUtc);
        var eligibleCount = await eligible.CountAsync(ct);
        var returnedCount = await BuildReturnedAfterRegistrationQuery(todayUtc).CountAsync(ct);

        return new AdminUserRetentionStats(DateOnly.FromDateTime(todayUtc), d1, d7, d30,
            new AdminRetentionMetric(eligibleCount, returnedCount));
    }

    private async Task<AdminRetentionMetric> GetDayRetentionAsync(int day, DateTime todayUtc, CancellationToken ct)
    {
        // The entire target UTC day must be over; today's incomplete day never enters either count.
        var cutoff = todayUtc.AddDays(-day);
        var eligible = await dbContext.Users.CountAsync(u => u.CreatedAtUtc < cutoff, ct);
        var returned = await BuildDayRetainedUsersQuery(day, todayUtc).CountAsync(ct);
        return new AdminRetentionMetric(eligible, returned);
    }

    internal IQueryable<User> BuildDayRetainedUsersQuery(int day, DateTime todayUtc)
    {
        var cutoff = todayUtc.AddDays(-day);
        return dbContext.Users.AsNoTracking().Where(u => u.CreatedAtUtc < cutoff
            && u.RefreshTokens.Any(t => DateOnly.FromDateTime(t.CreatedAtUtc)
                == DateOnly.FromDateTime(u.CreatedAtUtc).AddDays(day)));
    }

    internal IQueryable<User> BuildReturnedAfterRegistrationQuery(DateTime todayUtc) =>
        dbContext.Users.AsNoTracking().Where(u => u.CreatedAtUtc < todayUtc
            && u.RefreshTokens.Any(t => DateOnly.FromDateTime(t.CreatedAtUtc) > DateOnly.FromDateTime(u.CreatedAtUtc)
                && t.CreatedAtUtc < todayUtc));

    public async Task<IReadOnlyDictionary<DateOnly, int>> GetNewUsersPerDayAsync(
        DateTime fromUtcDate,
        CancellationToken ct = default)
    {
        var rows = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.CreatedAtUtc >= fromUtcDate)
            .GroupBy(u => u.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => r.Count);
    }

    public async Task<IReadOnlyDictionary<DateOnly, int>> GetDailyActiveUsersPerDayAsync(
        DateTime fromUtcDate,
        CancellationToken ct = default)
    {
        // shortcut: history depends on retained refresh tokens, use daily aggregates if session/account deletion must not change it.
        var rows = await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.CreatedAtUtc >= fromUtcDate)
            .GroupBy(t => t.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Select(t => t.UserId).Distinct().Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => r.Count);
    }

    private Task<int> CountActiveUsersSinceAsync(DateTime sinceUtc, CancellationToken ct) =>
        dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.CreatedAtUtc >= sinceUtc)
            .Select(t => t.UserId)
            .Distinct()
            .CountAsync(ct);
}
