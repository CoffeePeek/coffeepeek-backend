using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.PublicAddresses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Read-only bridge between published UUIDs and visible public addresses.</summary>
[ApiController]
[Route("api/admin/shops")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class AdminShopPublicAddressesController(IPublicAddressService addresses) : ControllerBase
{
    /// <summary>Returns the canonical public identity for a visible published shop UUID.</summary>
    [HttpGet("{id:guid}/public-address")]
    [ProducesResponseType<Response<PublicAddressMetadata>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var results = await addresses.GetByIdsAsync(PublicAddressKind.CoffeeShop, [id], ct);
        var address = results.SingleOrDefault(a => a.EntityId == id);
        return address is null ? NotFound() : Ok(Response<PublicAddressMetadata>.Success(address));
    }

    /// <summary>Resolves a visible canonical slug or alias to its published shop identity.</summary>
    [HttpGet("by-slug/{slug}/public-address")]
    [ProducesResponseType<Response<PublicAddressMetadata>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.CoffeeShop, slug, ct);
        return address is null ? NotFound() : Ok(Response<PublicAddressMetadata>.Success(address));
    }
}
