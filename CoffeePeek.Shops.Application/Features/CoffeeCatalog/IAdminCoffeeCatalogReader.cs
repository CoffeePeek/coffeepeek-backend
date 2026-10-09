using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface IAdminCoffeeCatalogReader
{
    Task<PublicPage<AdminCoffeeDto>> AdminListAsync(int page, int pageSize, string? status, CancellationToken ct);
    Task<AdminCoffeeDto?> AdminGetAsync(Guid id, CancellationToken ct);
    Task<RoasterTagDto[]> AdminTagsAsync(CancellationToken ct);
    Task<RoasterTagDto[]> AdminRoasterTagsAsync(Guid roasterId, CancellationToken ct);
    Task<CoffeeImportRunDto[]> RunsAsync(int limit, CancellationToken ct);
}
