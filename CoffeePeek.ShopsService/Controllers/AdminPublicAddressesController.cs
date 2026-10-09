using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shops.Application.PublicAddresses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/admin/{entity:regex(^(shops|roasters|cities|coffee-zones|beans|equipments|brew-methods|shop-tags|coffees)$)}/public-addresses")]
[Authorize(Policy = RoleConsts.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPublicAddressesController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpPost("{id:guid}/initialize")]
    public async Task<ActionResult<PublicAddressMetadata>> Initialize(string entity, Guid id, ChangePublicAddressRequest request, CancellationToken ct)
    {
        if (request.ExpectedRevision != 0) throw new PublicAddressException("Initialization requires expectedRevision 0.", 400);
        return Ok(await bus.InvokeAsync<PublicAddressMetadata>(new ChangePublicAddressCommand(Kind(entity), id, request, userContext.GetUserIdOrThrow()), ct));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<PublicAddressMetadata>> Rename(string entity, Guid id, ChangePublicAddressRequest request, CancellationToken ct)
    {
        if (request.ExpectedRevision <= 0 || request.Slug is null) throw new PublicAddressException("Rename requires a slug and positive expectedRevision.", 400);
        return Ok(await bus.InvokeAsync<PublicAddressMetadata>(new ChangePublicAddressCommand(Kind(entity), id, request, userContext.GetUserIdOrThrow()), ct));
    }

    [HttpPost("backfill")]
    public async Task<ActionResult<PublicAddressBackfillResult>> Backfill(string entity, [FromQuery] Guid? afterId = null,
        [FromQuery] int limit = 50, [FromQuery] bool dryRun = true, CancellationToken ct = default) =>
        Ok(await bus.InvokeAsync<PublicAddressBackfillResult>(new BackfillPublicAddressesCommand(Kind(entity), afterId, limit, dryRun, userContext.GetUserIdOrThrow()), ct));

    // Every route entity maps explicitly; anything else is rejected instead of defaulting to a kind.
    private static PublicAddressKind Kind(string entity) => entity.ToLowerInvariant() switch
    {
        "shops" => PublicAddressKind.CoffeeShop,
        "roasters" => PublicAddressKind.Roaster,
        "cities" => PublicAddressKind.City,
        "coffee-zones" => PublicAddressKind.CoffeeZone,
        "beans" => PublicAddressKind.CoffeeBean,
        "coffees" => PublicAddressKind.CoffeeProduct,
        "equipments" => PublicAddressKind.Equipment,
        "brew-methods" => PublicAddressKind.BrewMethod,
        "shop-tags" => PublicAddressKind.ShopTag,
        _ => throw new PublicAddressException("Unknown address kind.", 400)
    };
}
