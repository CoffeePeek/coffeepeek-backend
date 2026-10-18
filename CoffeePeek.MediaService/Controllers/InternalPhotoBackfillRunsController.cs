using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/check-in-photo-backfill/runs")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalPhotoBackfillRunsController(PhotoBackfillRuns runs,
    IOptions<GatewayAuthOptions> auth) : ControllerBase
{
    private bool Trusted => GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], auth.Value.SecretKey);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePhotoBackfillRun request, CancellationToken ct)
    {
        if (!Trusted) return Unauthorized();
        try { return Ok(await runs.CreateAsync(request, ct)); }
        catch (ArgumentException) { return BadRequest("Invalid archive run bounds."); }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        if (!Trusted) return Unauthorized();
        var run = await runs.StatusAsync(id, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpPost("{id:guid}/advance")]
    public async Task<IActionResult> Advance(Guid id, CancellationToken ct)
    {
        if (!Trusted) return Unauthorized();
        var result = await runs.AdvanceAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/pause")]
    public Task<IActionResult> Pause(Guid id, CancellationToken ct) => SetPaused(id, true, ct);

    [HttpPost("{id:guid}/resume")]
    public Task<IActionResult> Resume(Guid id, CancellationToken ct) => SetPaused(id, false, ct);

    private async Task<IActionResult> SetPaused(Guid id, bool paused, CancellationToken ct)
    {
        if (!Trusted) return Unauthorized();
        var result = await runs.SetPausedAsync(id, paused, ct);
        if (result is null) return NotFound();
        return result.Outcome is "another_run_active" or "previous_archive_backlog" or "recipe_changed" ? Conflict(result) : Ok(result);
    }
}
