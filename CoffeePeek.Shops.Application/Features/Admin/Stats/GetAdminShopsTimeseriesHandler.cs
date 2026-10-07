using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shared.Kernel.Extentions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Shops.Application.Features.Admin.Stats;

public static class GetAdminShopsTimeseriesHandler
{
    public static async Task<Response<AdminShopsTimeseriesDto>> Handle(
        GetAdminShopsTimeseriesQuery query,
        IAdminStatsQueryRepository repository,
        CancellationToken ct)
    {
        var days = DailySeries.ClampDays(query.Days);
        var todayUtc = DateTime.UtcNow.Date;
        var today = DateOnly.FromDateTime(todayUtc);

        var counts = await repository.GetDailyCountsAsync(DailySeries.WindowStartUtc(todayUtc, days), ct);

        return Response<AdminShopsTimeseriesDto>.Success(new AdminShopsTimeseriesDto(
            Days: days,
            NewShops: Fill(today, days, counts.Shops),
            NewCheckIns: Fill(today, days, counts.CheckIns)));
    }

    private static List<AdminDailyCountDto> Fill(DateOnly today, int days, IReadOnlyDictionary<DateOnly, int> counts) =>
        DailySeries.Fill(today, days, counts).Select(x => new AdminDailyCountDto(x.Date, x.Count)).ToList();
}
