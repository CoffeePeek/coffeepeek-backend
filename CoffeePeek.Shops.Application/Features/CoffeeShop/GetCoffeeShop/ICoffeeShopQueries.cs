using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;

public interface ICoffeeShopQueries
{
    Task<(ShortShopDto[] Items, int TotalCount)> Search(SearchCoffeeShopsQuery request, CancellationToken ct);
    Task<CoffeeShopDetailsDto?> GetDetailsById(Guid id, CancellationToken ct);
    Task<GetShopsInBoundsResponse> GetMap(
        GetShopsInBoundsQuery query,
        MapClusteringOptions options,
        CancellationToken ct = default);
}
