using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Application.Features.CheckIn;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Bounded internal inventory of canonical Community photo bindings.</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/check-in-photo-backfill")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalCheckInPhotoBackfillController(ICheckInPhotoBackfillCandidates candidates,
    IOptions<GatewayAuthOptions> auth, TimeProvider clock) : ControllerBase
{
    /// <summary>Observe one page without changing content, moderation or photo storage.</summary>
    [HttpPost("candidates")]
    public async Task<IActionResult> Candidates([FromBody] PhotoBackfillPageRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], auth.Value.SecretKey)) return Unauthorized();
        if (request is null || !request.IsValid(clock.GetUtcNow().UtcDateTime)) return BadRequest();
        return Ok(await candidates.ReadAsync(request, ct));
    }
}
