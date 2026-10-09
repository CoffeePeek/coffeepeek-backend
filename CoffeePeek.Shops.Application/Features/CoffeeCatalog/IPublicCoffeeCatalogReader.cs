using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface IPublicCoffeeCatalogReader
{
    Task<Dictionary<Guid, (int CoffeeShopsCount, int CoffeeProductsCount)>> RoasterCountsAsync(Guid[] ids, CancellationToken ct);
    Task<CountryDto[]> CountriesAsync(CancellationToken ct);
    Task<RoasterTagDto[]> PublicTagsAsync(CancellationToken ct);
    Task<PublicRoasterDto[]> RoastersAsync(string[] tags, CancellationToken ct);
    Task<CoffeeRoasterOverviewDto> RoasterOverviewAsync(string slug, CancellationToken ct);
    Task<PublicPage<CoffeeProductDto>> ListAsync(CoffeeCatalogFilter filter, CancellationToken ct);
    Task<CoffeeProductDto?> GetAsync(string slug, CancellationToken ct);
}
