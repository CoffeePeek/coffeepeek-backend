using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore
{
    public async Task<GetShopsInBoundsResponse> MapShopsAsync(CoffeeShopMapRequest request, Guid? userId, CancellationToken ct)
    {
        var match = await MatchShopsAsync(request.Q, request.Filters ?? new(), userId, ct, includeTotal: false);
        var bounds = request.Bounds;
        var query = new GetShopsInBoundsQuery(bounds.MinLat, bounds.MinLon, bounds.MaxLat, bounds.MaxLon, request.Zoom);
        return await new DiscoveryMapReader(db).ReadAsync(match.Hits.Select(hit => hit.Shop), query,
            mapOptions?.Value ?? new MapClusteringOptions(), ct, hideEmptyZones: true);
    }
}
