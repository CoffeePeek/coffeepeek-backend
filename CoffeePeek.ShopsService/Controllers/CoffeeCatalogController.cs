using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoffeeCatalogController(ICoffeeCatalogStore store) : ControllerBase
{
    [HttpGet("api/Catalogs/coffee-origin-countries")]
    public async Task<IActionResult> Countries(CancellationToken ct) => Ok(Response<CountryDto[]>.Success(await store.CountriesAsync(ct)));
    [HttpGet("api/Catalogs/roaster-tags")]
    public async Task<IActionResult> Tags(CancellationToken ct) => Ok(Response<PublicTagDto[]>.Success(
        (await store.TagsAsync(false, ct)).Select(t => new PublicTagDto(t.Slug, t.Name, t.Description, t.SortOrder)).ToArray()));
    [HttpGet("api/roasters")]
    [ProducesResponseType<Response<PublicRoasterDto[]>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Roasters(CancellationToken ct, [FromQuery] string[]? tags = null) =>
        Ok(Response<PublicRoasterDto[]>.Success(await store.RoastersAsync(tags ?? [], ct)));
    [HttpGet("api/coffees")]
    public async Task<IActionResult> List([FromQuery] CoffeeCatalogFilter filter, CancellationToken ct) =>
        Ok(Response<PublicPage<CoffeeProductDto>>.Success(await store.ListAsync(filter, ct)));
    [HttpGet("api/roasters/{slug}/coffees")]
    public async Task<IActionResult> ForRoaster(string slug, [FromQuery] CoffeeCatalogFilter filter, CancellationToken ct)
    { filter.RoasterSlug = slug; return await List(filter, ct); }
    [HttpGet("api/coffees/{slug}")]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var item = await store.GetAsync(slug, ct);
        return item is null ? NotFound() : Ok(Response<CoffeeProductDto>.Success(item));
    }
}
