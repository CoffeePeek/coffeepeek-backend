using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface ICoffeeCatalogStore
{
    Task<Dictionary<Guid, (int CoffeeShopsCount, int CoffeeProductsCount)>> RoasterCountsAsync(Guid[] ids, CancellationToken ct);
    Task<CountryDto[]> CountriesAsync(CancellationToken ct);
    Task<RoasterTagDto[]> TagsAsync(bool admin, CancellationToken ct);
    Task<RoasterTagDto> CreateTagAsync(CreateRoasterTagRequest request, CancellationToken ct);
    Task<RoasterTagDto?> EditTagAsync(Guid id, RoasterTagEdit request, CancellationToken ct);
    Task SetRoasterTagsAsync(Guid roasterId, Guid[] tagIds, Guid actorId, CancellationToken ct);
    Task<PublicTagDto[]> RoasterTagsAsync(Guid roasterId, CancellationToken ct);
    Task<RoasterTagDto[]> AdminRoasterTagsAsync(Guid roasterId, CancellationToken ct);
    Task<CoffeeRoasterOverviewDto> RoasterOverviewAsync(string slug, CancellationToken ct);
    Task<bool> ArchiveAsync(Guid id, CancellationToken ct);
    Task<PublicPage<CoffeeRoasterPageDto>> RoastersAsync(string[] tags, int page, int pageSize, CancellationToken ct);
    Task<PublicPage<CoffeeProductDto>> ListAsync(CoffeeCatalogFilter filter, CancellationToken ct);
    Task<CoffeeProductDto?> GetAsync(string slug, CancellationToken ct);
    Task<PublicPage<AdminCoffeeDto>> AdminListAsync(int page, int pageSize, string? status, CancellationToken ct);
    Task<AdminCoffeeDto?> AdminGetAsync(Guid id, CancellationToken ct);
    Task<AdminCoffeeDto> CreateAsync(CreateCoffeeRequest request, CancellationToken ct);
    Task<AdminCoffeeDto?> EditAsync(Guid id, CoffeeEditRequest request, CancellationToken ct);
    Task<CoffeeImportRunDto[]> RunsAsync(int limit, CancellationToken ct);
    Task<CoffeeImportResult> ImportAsync(CoffeeSnapshotImportRequest request, CancellationToken ct);
    Task RecordFailureAsync(CoffeeImportFailureDto failure, CancellationToken ct);
}
