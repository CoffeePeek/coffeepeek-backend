using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("internal/roasters")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalRoasterExistenceController(
    IQueryRoasterRepository roasters,
    IOptions<GatewayAuthOptions> options) : ControllerBase
{
    [HttpGet("exists")]
    public async Task<IActionResult> Exists([FromQuery] string name, CancellationToken ct)
    {
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], options.Value.SecretKey))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(name) || name.Length > BusinessConstants.MaxRoasterNameLength)
            return BadRequest();

        return Ok(await roasters.ExistsByNameAsync(name, ct));
    }
}
