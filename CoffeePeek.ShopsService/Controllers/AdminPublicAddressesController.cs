using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shops.Application.PublicAddresses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/admin/{entity:regex(^(shops|roasters|cities|coffee-zones)$)}/public-addresses")]
[Authorize(Policy = RoleConsts.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPublicAddressesController(IPublicAddressService addresses, IUserContext userContext) : ControllerBase
{
    [HttpPost("{id:guid}/initialize")]
    public async Task<ActionResult<PublicAddressMetadata>> Initialize(string entity, Guid id, ChangePublicAddressRequest request, CancellationToken ct)
    {
        if (request.ExpectedRevision != 0) throw new PublicAddressException("Initialization requires expectedRevision 0.", 400);
        return Ok(await addresses.ChangeAsync(Kind(entity), id, request, userContext.GetUserIdOrThrow(), ct));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<PublicAddressMetadata>> Rename(string entity, Guid id, ChangePublicAddressRequest request, CancellationToken ct)
    {
        if (request.ExpectedRevision <= 0 || request.Slug is null) throw new PublicAddressException("Rename requires a slug and positive expectedRevision.", 400);
        return Ok(await addresses.ChangeAsync(Kind(entity), id, request, userContext.GetUserIdOrThrow(), ct));
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<PublicAddressHistoryItem[]>> History(string entity, Guid id, [FromQuery] long afterRevision = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await addresses.HistoryAsync(Kind(entity), id, afterRevision, limit, ct));

    [HttpPost("backfill")]
    public async Task<ActionResult<PublicAddressBackfillResult>> Backfill(string entity, [FromQuery] Guid? afterId = null,
        [FromQuery] int limit = 50, [FromQuery] bool dryRun = true, CancellationToken ct = default) =>
        Ok(await addresses.BackfillAsync(Kind(entity), afterId, limit, dryRun, userContext.GetUserIdOrThrow(), ct));

    // Every route entity maps explicitly; anything else is rejected instead of defaulting to a kind.
    private static PublicAddressKind Kind(string entity) => entity.ToLowerInvariant() switch
    {
        "shops" => PublicAddressKind.CoffeeShop,
        "roasters" => PublicAddressKind.Roaster,
        "cities" => PublicAddressKind.City,
        "coffee-zones" => PublicAddressKind.CoffeeZone,
        _ => throw new PublicAddressException("Unknown address kind.", 400)
    };
}
