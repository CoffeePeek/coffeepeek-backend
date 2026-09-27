using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Application.PublicAddresses;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/roasters")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicRoasterAddressesController(IPublicAddressService addresses, IMessageBus bus) : ControllerBase
{
    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.Roaster, slug, ct);
        if (address is null) return NotFound();
        var response = await bus.InvokeAsync<Response<RoasterDetailsDto>>(new GetRoasterByIdQuery(address.EntityId), ct);
        return response is { IsSuccess: true, Data: not null }
            ? Ok(new PublicAddressEnvelope<RoasterDetailsDto>(response.Data, address)) : NotFound();
    }

    [HttpGet("public-addresses")]
    public async Task<ActionResult<PublicAddressMetadata[]>> GetBatch([FromQuery] Guid[] ids, CancellationToken ct) =>
        Ok(await addresses.GetByIdsAsync(PublicAddressKind.Roaster, ids, ct));

    [HttpGet("{id:guid}/public-address")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await addresses.GetByIdsAsync(PublicAddressKind.Roaster, [id], ct);
        return result.Length == 0 ? NotFound() : Ok(result[0]);
    }
}
