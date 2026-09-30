using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.PublicAddresses;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/CoffeeShops")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicCoffeeShopAddressesController(IPublicAddressService addresses, IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.CoffeeShop, slug, ct);
        if (address is null) return NotFound();
        var response = await bus.InvokeAsync<Response<GetCoffeeShopResponse>>(new GetCoffeeShopQuery(address.EntityId, userContext.GetUserId()), ct);
        return response is { IsSuccess: true, Data: not null }
            ? Ok(new PublicAddressEnvelope<GetCoffeeShopResponse>(response.Data, address)) : NotFound();
    }

    [HttpGet("public-addresses")]
    public async Task<ActionResult<PublicAddressMetadata[]>> GetBatch([FromQuery] Guid[] ids, CancellationToken ct) =>
        Ok(await addresses.GetByIdsAsync(PublicAddressKind.CoffeeShop, ids, ct));

    [HttpGet("{id:guid}/public-address")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await addresses.GetByIdsAsync(PublicAddressKind.CoffeeShop, [id], ct);
        return result.Length == 0 ? NotFound() : Ok(result[0]);
    }
}
