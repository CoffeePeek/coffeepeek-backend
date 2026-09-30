using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("internal/public-addresses/users")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalPublicAddressesController(IProfileAddressService addresses, IOptions<GatewayAuthOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetBatch([FromQuery] Guid[] ids, CancellationToken ct) => Trusted()
        ? Ok(await addresses.GetByIdsAsync(ids, ct)) : Unauthorized();

    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> Resolve(string slug, CancellationToken ct)
    {
        if (!Trusted()) return Unauthorized();
        var result = await addresses.ResolveAsync(slug, ct);
        return result is null ? NotFound() : Ok(result);
    }

    private bool Trusted() => GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], options.Value.SecretKey);
}
