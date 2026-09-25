using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shared.Kernel.Extentions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Admin.Stats;

public static class GetAdminUsersTimeseriesHandler
{
    public static async Task<Response<AdminUsersTimeseriesDto>> Handle(
        GetAdminUsersTimeseriesQuery query,
        IAdminUserQueryRepository userRepository,
        CancellationToken ct)
    {
        var days = DailySeries.ClampDays(query.Days);
        var todayUtc = DateTime.UtcNow.Date;
        var from = DailySeries.WindowStartUtc(todayUtc, days);

        var counts = await userRepository.GetNewUsersPerDayAsync(from, ct);

        var series = DailySeries.Fill(DateOnly.FromDateTime(todayUtc), days, counts)
            .Select(x => new AdminDailyCountDto(x.Date, x.Count))
            .ToList();

        return Response<AdminUsersTimeseriesDto>.Success(new AdminUsersTimeseriesDto(days, series));
    }
}
