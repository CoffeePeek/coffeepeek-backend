using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.ShopsService.Discovery;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DiscoveryController(IDiscoveryQueryStore queries, IDiscoveryClassificationStore classification,
    ICoffeeCatalogStore catalog, IUserContext user) : ControllerBase
{
    [HttpPost("api/v1/discovery/search")]
    public Task<IActionResult> Discovery(DiscoverySearchRequest request, CancellationToken ct) =>
        Search(request, () => queries.SearchDiscoveryAsync(request, user.GetUserId(), ct));
    [HttpPost("api/v1/coffee-shops/search")]
    public Task<IActionResult> Shops(CoffeeShopSearchRequest request, CancellationToken ct) =>
        Search(request, () => queries.SearchShopsAsync(request, user.GetUserId(), ct));
    [HttpPost("api/v1/roasters/search")]
    public Task<IActionResult> Roasters(RoasterSearchRequest request, CancellationToken ct) =>
        Search(request, () => queries.SearchRoastersAsync(request, user.GetUserId(), ct));
    [HttpPost("api/v1/coffees/search")]
    public Task<IActionResult> Coffees(CoffeeSearchRequest request, CancellationToken ct) =>
        Search(request, () => queries.SearchCoffeesAsync(request, ct));
    [HttpPost("api/v1/coffees/facets")]
    public Task<IActionResult> CoffeeFacets(CoffeeFacetsRequest request, CancellationToken ct) =>
        Search(request, () => queries.CoffeeFacetsAsync(request, ct));
    [HttpPost("api/v1/roasters/facets")]
    public Task<IActionResult> RoasterFacets(RoasterFacetsRequest request, CancellationToken ct) =>
        Search(request, () => queries.RoasterFacetsAsync(request, user.GetUserId(), ct));
    [HttpGet("api/v1/catalogs/coffee-filter-values")]
    public async Task<IActionResult> Values(CancellationToken ct) =>
        Ok(Response<FilterGroup[]>.Success(await classification.PublicValuesAsync(ct)));
    [HttpGet("api/v1/catalogs/roaster-tags")]
    public async Task<IActionResult> Tags(CancellationToken ct) => Ok(Response<PublicTag[]>.Success(
        (await catalog.TagsAsync(false, ct)).Select(t => new PublicTag { Slug = t.Slug, Name = t.Name,
            Description = t.Description, SortOrder = t.SortOrder }).ToArray()));
    [HttpGet("api/v1/coffees/{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken ct)
    {
        var result = await queries.CoffeeDetailsAsync(slug, ct);
        return result is null ? NotFound(new ErrorResponse("Coffee not found.", "NOT_FOUND") { StatusCode = 404 })
            : Ok(Response<CoffeeDetails>.Success(result));
    }
    private async Task<IActionResult> Search<T>(object request, Func<Task<T>> read)
    {
        var errors = DiscoveryRequestValidation.Validate(request);
        if (errors.Count > 0) return BadRequest(new ErrorResponse("Invalid search filters.", "INVALID_FILTER", errors) { StatusCode = 400 });
        var personal = request switch
        {
            CoffeeShopSearchRequest r => r.Filters?.FavoritesOnly == true || r.Filters?.VisitedOnly == true,
            RoasterSearchRequest r => r.Filters?.FavoritesOnly == true,
            RoasterFacetsRequest r => r.Filters?.FavoritesOnly == true,
            _ => false
        };
        if (personal && user.GetUserId() is null)
            return Unauthorized(new ErrorResponse("Personal filters require authentication.", "UNAUTHORIZED") { StatusCode = 401 });
        return Ok(Response<T>.Success(await read()));
    }
}
