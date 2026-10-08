using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Persistance.Menu;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/admin/menu-ocr-jobs")]
[Authorize(Policy = RoleConsts.Admin)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class AdminMenuOcrRecoveryController(
    MenuOcrJobStore jobs,
    IUserContext userContext,
    ILogger<AdminMenuOcrRecoveryController> logger) : ControllerBase
{
    [HttpPost("{jobId:guid}/recover-unknown-outcome")]
    public async Task<IActionResult> RecoverUnknownOutcome(Guid jobId, CancellationToken ct)
    {
        var status = await jobs.RecoverUnknownOutcomeAsync(jobId, DateTime.UtcNow, ct);
        if (status is null)
            return NotFound();
        logger.LogWarning("Operator {UserId} recovered OCR job {JobId} as {Status}",
            userContext.GetUserIdOrThrow(), jobId, status.Value);
        return Ok(new { jobId, status = status.Value.ToString() });
    }
}
