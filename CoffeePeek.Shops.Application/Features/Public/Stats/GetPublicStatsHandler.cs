using CoffeePeek.Contract.Dtos.Public;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.Features.Public.Stats;

public static class GetPublicStatsHandler
{
    public static async Task<Response<PublicPlatformStatsDto>> Handle(
        GetPublicStatsQuery _,
        IPublicStatsQueryRepository repository,
        CancellationToken ct)
    {
        var stats = await repository.GetStatsAsync(ct);
        var dto = new PublicPlatformStatsDto(
            TotalCoffeeShops: stats.TotalCoffeeShops,
            TotalCheckIns: stats.TotalCheckIns,
            AverageRating: stats.AverageRating);

        return Response<PublicPlatformStatsDto>.Success(dto);
    }
}
