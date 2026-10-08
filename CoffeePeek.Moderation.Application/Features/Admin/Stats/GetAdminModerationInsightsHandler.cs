using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Admin.Stats;

public static class GetAdminModerationInsightsHandler
{
    public static async Task<Response<AdminModerationInsightsDto>> Handle(
        GetAdminModerationInsightsQuery _,
        IAdminModerationStatsQueryRepository repository,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var insights = await repository.GetInsightsAsync(now, ct);

        var queues = insights.Queues
            .Select(q => new AdminModerationQueueDto(
                q.Queue,
                q.Pending,
                q.OldestPendingCreatedAtUtc is { } oldest ? RoundHours((now - oldest).TotalHours) : null))
            .ToList();

        var oldestOverall = queues
            .Where(q => q.OldestPendingHours.HasValue)
            .Select(q => q.OldestPendingHours)
            .DefaultIfEmpty(null)
            .Max();

        return Response<AdminModerationInsightsDto>.Success(new AdminModerationInsightsDto(
            Sla: new AdminModerationSlaDto(
                insights.CheckInsModerated,
                insights.AvgCheckInModerationHours is { } avg ? RoundHours(avg) : null),
            Queues: queues,
            OldestPendingHours: oldestOverall,
            TopModerators30Days: insights.Workload
                .Select(w => new AdminModeratorWorkloadDto(w.ModeratorUserId, w.Total, w.Approved, w.Rejected))
                .ToList()));
    }

    private static double RoundHours(double hours) => Math.Round(hours, 1, MidpointRounding.AwayFromZero);
}
