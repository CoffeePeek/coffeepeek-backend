using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/check-in-photo-backfill/inspect")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalPhotoBackfillInventoryController(PhotoBackfillInspector inspector, IOptions<GatewayAuthOptions> auth)
    : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Inspect([FromBody] PhotoBackfillCandidate[] candidates,
        [FromQuery] bool verifyContent = true, CancellationToken ct = default)
    {
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], auth.Value.SecretKey))
            return Unauthorized();
        try { return Ok(await inspector.InspectAsync(candidates, verifyContent, ct)); }
        catch (ArgumentException) { return BadRequest(); }
    }
}
