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
        var query = dbContext.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .Include(u => u.PhotoMetadata)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.Username.Value.ToLower().Contains(term) ||
                u.Credentials.Email.Value.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.Roles.Any(r => r.Name == role));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue))
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<AdminUserStats> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Sequential on purpose: a single scoped DbContext does not support concurrent queries.
        var totalUsers = await dbContext.Users.CountAsync(ct);
        var blockedUsers = await dbContext.Users.CountAsync(u => u.IsBlocked && !u.IsSoftDelete, ct);
        var deletedUsers = await dbContext.Users.CountAsync(u => u.IsSoftDelete, ct);
        var activeUsers = await dbContext.Users.CountAsync(u => !u.IsBlocked && !u.IsSoftDelete, ct);
        var registeredToday = await dbContext.Users.CountAsync(u => u.CreatedAtUtc >= today, ct);
        var emailConfirmedUsers = await dbContext.Users.CountAsync(u => u.Credentials.EmailConfirmed, ct);
        var googleUsers = await dbContext.Users.CountAsync(u => u.Credentials.OAuthProvider != null, ct);

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
            GoogleUsers: googleUsers);
    }

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

    private Task<int> CountActiveUsersSinceAsync(DateTime sinceUtc, CancellationToken ct) =>
        dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.CreatedAtUtc >= sinceUtc)
            .Select(t => t.UserId)
            .Distinct()
            .CountAsync(ct);
}
