using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>
/// Public city addresses. Every existing city is public, the same set as <c>GET api/Catalogs/cities</c>.
/// Reads go through the address service and an AsNoTracking city read, never the <c>city:list:all</c> cache.
/// </summary>
[ApiController]
[Route("api/Catalogs/cities")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicCityAddressesController(IPublicAddressService addresses, IQueryCityRepository cities) : ControllerBase
{
    /// <summary>Resolves a city slug or alias to the city and its current canonical address.</summary>
    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.City, slug, ct);
        if (address is null) return NotFound();
        // The city can be deleted between resolve and read; treat it like an unknown slug.
        var city = await cities.GetById(address.EntityId, ct);
        if (city is null) return NotFound();
        return Ok(new PublicAddressEnvelope<CityDto>(new CityDto { Id = city.Id, Name = city.Name }, address));
    }

    /// <summary>Returns canonical addresses for up to 100 city ids; unknown ids are omitted.</summary>
    [HttpGet("public-addresses")]
    public async Task<ActionResult<PublicAddressMetadata[]>> GetBatch([FromQuery] Guid[] ids, CancellationToken ct) =>
        Ok(await addresses.GetByIdsAsync(PublicAddressKind.City, ids, ct));

    /// <summary>Returns the canonical address for a legacy city GUID.</summary>
    [HttpGet("{id:guid}/public-address")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await addresses.GetByIdsAsync(PublicAddressKind.City, [id], ct);
        return result.Length == 0 ? NotFound() : Ok(result[0]);
    }
}
