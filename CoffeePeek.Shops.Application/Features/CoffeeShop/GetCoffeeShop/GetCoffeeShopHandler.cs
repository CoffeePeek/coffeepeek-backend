using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;

public static class GetCoffeeShopHandler
{
    public static async Task<Response<GetCoffeeShopResponse>> Handle(GetCoffeeShopQuery query,
        ICoffeeShopQueries shopQueries, IQueryCheckInRepository repository, ICheckInQueries checkIns, CancellationToken ct)
    {
        var shop = await shopQueries.GetDetailsById(query.Id, ct);
        if (shop == null) return Response<GetCoffeeShopResponse>.Error("Shop not found");
        var (rating, count) = await checkIns.GetStatsByShopAsync(query.Id, ct);
        var page = await checkIns.GetPublicAsync(query.UserId, null, query.Id, null, 10, null, ct);
        shop = shop with { Rating = rating, CheckInCount = count, CheckIns = page.Items };
        if (query.UserId is { } userId)
            shop = shop with
            {
                IsVisited = await repository.Exists(userId, query.Id, ct),
                UserCheckIns = await checkIns.GetByUserAndShopAsync(userId, query.Id, 1, 10, ct)
            };
        return Response<GetCoffeeShopResponse>.Success(new GetCoffeeShopResponse(shop));
    }
}
