using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public interface IDiscoveryQueryStore
{
    Task<CoffeeShopPage> SearchShopsAsync(CoffeeShopSearchRequest request, Guid? userId, CancellationToken ct);
    Task<CoffeePage> SearchCoffeesAsync(CoffeeSearchRequest request, CancellationToken ct);
    Task<CoffeeFacetsResult> CoffeeFacetsAsync(CoffeeFacetsRequest request, CancellationToken ct);
    Task<CoffeeDetails?> CoffeeDetailsAsync(string slug, CancellationToken ct);
}
