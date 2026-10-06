using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public interface IDiscoveryQueryStore
{
    Task<DiscoverySearchResult> SearchDiscoveryAsync(DiscoverySearchRequest request, Guid? userId, CancellationToken ct);
    Task<CoffeeShopPage> SearchShopsAsync(CoffeeShopSearchRequest request, Guid? userId, CancellationToken ct);
    Task<RoasterPage> SearchRoastersAsync(RoasterSearchRequest request, Guid? userId, CancellationToken ct);
    Task<CoffeePage> SearchCoffeesAsync(CoffeeSearchRequest request, CancellationToken ct);
    Task<CoffeeFacetsResult> CoffeeFacetsAsync(CoffeeFacetsRequest request, CancellationToken ct);
    Task<RoasterFacetsResult> RoasterFacetsAsync(RoasterFacetsRequest request, Guid? userId, CancellationToken ct);
    Task<CoffeeDetails?> CoffeeDetailsAsync(string slug, CancellationToken ct);
}
