using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

// All queries are sequential: the repository shares one scoped ModerationDbContext, which does not support concurrency.
public class AdminModerationStatsQueryRepository(ModerationDbContext dbContext) : IAdminModerationStatsQueryRepository
{
    private const int WindowDays = 30;
    private const int TopModerators = 10;

    public async Task<(int PendingShops, int PendingReviews)> GetStatsAsync(CancellationToken ct = default)
    {
        var pendingShops = await dbContext.ModerationShops
            .CountAsync(s => s.ModerationStatus == ModerationStatus.Pending, ct);

        var pendingReviews = await dbContext.ModerationReviews
            .CountAsync(r => r.ModerationStatus == ModerationStatus.Pending, ct);

        return (pendingShops, pendingReviews);
    }

    public async Task<AdminModerationInsights> GetInsightsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var since = nowUtc.AddDays(-WindowDays);

        // Bounded to 30 days and averaged in memory to avoid provider-specific interval translation.
        var moderated = await dbContext.ModerationReviews
            .AsNoTracking()
            .Where(r => r.ModeratedAt != null
                        && r.ModeratedAt >= since
                        && r.ModerationStatus != ModerationStatus.Pending)
            .Select(r => new { r.CreatedAtUtc, r.ModeratedAt })
            .ToListAsync(ct);

        double? avgHours = moderated.Count == 0
            ? null
            : moderated.Average(r => (r.ModeratedAt!.Value - r.CreatedAtUtc).TotalHours);

        var queues = new List<AdminModerationQueueSnapshot>
        {
            await QueueAsync("shops",
                dbContext.ModerationShops.Where(s => s.ModerationStatus == ModerationStatus.Pending)
                    .Select(s => (DateTime?)s.CreatedAtUtc), ct),
            await QueueAsync("reviews",
                dbContext.ModerationReviews.Where(r => r.ModerationStatus == ModerationStatus.Pending)
                    .Select(r => (DateTime?)r.CreatedAtUtc), ct),
            await QueueAsync("roasters",
                dbContext.ModerationRoasters.Where(r => r.ModerationStatus == ModerationStatus.Pending)
                    .Select(r => (DateTime?)r.CreatedAtUtc), ct),
            await QueueAsync("changeRequests",
                dbContext.ShopChangeRequests.Where(r => r.Status == ModerationStatus.Pending)
                    .Select(r => (DateTime?)r.CreatedAtUtc), ct),
            await QueueAsync("issueReports",
                dbContext.ShopIssueReports.Where(r => r.Status == ShopIssueReportStatus.Submitted)
                    .Select(r => (DateTime?)r.CreatedAtUtc), ct)
        };

        var workload = await dbContext.ModerationAuditLogs
            .AsNoTracking()
            .Where(a => a.CreatedAtUtc >= since)
            .GroupBy(a => a.ModeratorUserId)
            .Select(g => new
            {
                ModeratorUserId = g.Key,
                Total = g.Count(),
                Approved = g.Count(a => a.Action == ModerationAuditAction.Approved),
                Rejected = g.Count(a => a.Action == ModerationAuditAction.Rejected)
            })
            .OrderByDescending(x => x.Total)
            .Take(TopModerators)
            .ToListAsync(ct);

        return new AdminModerationInsights(
            moderated.Count,
            avgHours,
            queues,
            workload.Select(w => new AdminModeratorWorkload(w.ModeratorUserId, w.Total, w.Approved, w.Rejected))
                .ToList());
    }

    private static async Task<AdminModerationQueueSnapshot> QueueAsync(
        string name,
        IQueryable<DateTime?> pendingCreatedAt,
        CancellationToken ct)
    {
        var pending = await pendingCreatedAt.CountAsync(ct);
        var oldest = pending == 0 ? null : await pendingCreatedAt.MinAsync(ct);
        return new AdminModerationQueueSnapshot(name, pending, oldest);
    }
}
