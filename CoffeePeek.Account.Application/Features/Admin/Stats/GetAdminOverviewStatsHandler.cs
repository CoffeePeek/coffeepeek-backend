using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Admin.Stats;

public static class GetAdminOverviewStatsHandler
{
    public static async Task<Response<AdminOverviewStatsDto>> Handle(
        GetAdminOverviewStatsQuery _,
        IAdminUserQueryRepository userRepository,
        IAdminStatsClient statsClient,
        CancellationToken ct)
    {
        var userStats = await userRepository.GetStatsAsync(ct);
        var platform = await statsClient.GetPlatformStatsAsync(ct);

        return Response<AdminOverviewStatsDto>.Success(new AdminOverviewStatsDto(
            TotalUsers: userStats.TotalUsers,
            UsersRegisteredToday: userStats.RegisteredToday,
            ActiveUsers: userStats.ActiveUsers,
            BlockedUsers: userStats.BlockedUsers,
            TotalCoffeeShops: platform.TotalCoffeeShops,
            TotalCheckIns: platform.TotalCheckIns,
            PendingModerationShops: platform.PendingModerationShops,
            PendingModerationCheckIns: platform.PendingModerationCheckIns,
            NewCoffeeShopsToday: platform.NewCoffeeShopsToday,
            NewCheckInsToday: platform.NewCheckInsToday,
            Import: new AdminImportOverviewStatsDto(
                platform.ImportPending,
                platform.ImportPublished,
                platform.ImportRejected,
                platform.ImportSkipped,
                platform.ImportInFeed),
            ShopsAvailable: platform.ShopsAvailable,
            ModerationAvailable: platform.ModerationAvailable,
            DailyActiveUsers: userStats.DailyActiveUsers,
            WeeklyActiveUsers: userStats.WeeklyActiveUsers,
            MonthlyActiveUsers: userStats.MonthlyActiveUsers,
            DeletedUsers: userStats.DeletedUsers,
            EmailConfirmedUsers: userStats.EmailConfirmedUsers,
            GoogleUsers: userStats.GoogleUsers,
            AppleUsers: userStats.AppleUsers,
            EmailUsers: userStats.EmailUsers,
            OtherAuthUsers: userStats.OtherAuthUsers,
            Retention: userStats.Retention is { } retention
                ? new AdminUserRetentionStatsDto(retention.AsOfDateUtc, ToDto(retention.D1),
                    ToDto(retention.D7), ToDto(retention.D30), ToDto(retention.AfterRegistration))
                : null));
    }

    private static AdminRetentionMetricDto ToDto(AdminRetentionMetric metric) =>
        new(metric.EligibleUsers, metric.ReturnedUsers, metric.RatePercent);
}
