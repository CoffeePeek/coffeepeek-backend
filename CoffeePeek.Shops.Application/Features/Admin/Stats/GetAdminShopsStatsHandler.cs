using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Shops.Application.Features.Admin.Stats;

public static class GetAdminShopsStatsHandler
{
    public static async Task<Response<AdminServiceStatsDto>> Handle(
        GetAdminShopsStatsQuery _,
        IAdminStatsQueryRepository repository,
        CancellationToken ct)
    {
        var stats = await repository.GetStatsAsync(ct);

        return Response<AdminServiceStatsDto>.Success(new AdminServiceStatsDto(
            TotalCoffeeShops: stats.TotalCoffeeShops,
            TotalCheckIns: stats.TotalCheckIns,
            NewCoffeeShopsToday: stats.NewCoffeeShopsToday,
            NewCheckInsToday: stats.NewCheckInsToday));
    }
}
