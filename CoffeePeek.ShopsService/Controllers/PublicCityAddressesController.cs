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
    /// <summary>Resolves the current city slug to the city and its public address.</summary>
    [HttpGet("by-slug/{slug}")]
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.City, slug, ct);
        if (address is null) return NotFound();
        // The city can be deleted between resolve and read; treat it like an unknown slug.
        var city = await cities.GetById(address.EntityId, ct);
        if (city is null) return NotFound();
        return Ok(new PublicCityDto(PublicAddressDto.From(address), city.Name));
    }

}
