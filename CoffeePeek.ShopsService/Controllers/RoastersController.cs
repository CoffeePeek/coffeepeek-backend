using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Public roaster profile — address, contact, photos, about, and the shops carrying it.</summary>
[ApiController]
[Route("api/roasters")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RoastersController(PublicDiscoveryService discovery) : ControllerBase
{
    [HttpGet("{slug}")]
    [HttpGet("by-slug/{slug}")]
    [ProducesResponseType<Response<PublicRoasterDetailsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string slug, CancellationToken ct)
    {
        var data = await discovery.GetRoasterAsync(slug, ct);
        return data is null ? NotFound() : Ok(Response<PublicRoasterDetailsDto>.Success(data));
    }
}
