using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;

public class GetShopsInBoundsHandler
{
    public static async Task<Response<GetShopsInBoundsResponse>> Handle(
        GetShopsInBoundsQuery query,
        ICoffeeShopQueries shopQueries,
        IOptions<MapClusteringOptions> options,
        CancellationToken cancellationToken)
    {
        var response = await shopQueries.GetMap(query, options.Value, cancellationToken);
        return Response<GetShopsInBoundsResponse>.Success(response);
    }
}
