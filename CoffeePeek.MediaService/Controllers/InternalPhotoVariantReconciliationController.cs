using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[Route("internal/check-in-photo-variants/replay")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalPhotoVariantReconciliationController(MediaDbContext db, PhotoVariantReads reads,
    IDbContextOutbox outbox, IOptions<GatewayAuthOptions> auth) : ControllerBase
{
    /// <summary>Republish one bounded page of current Ready manifests through the durable outbox.</summary>
    [HttpPost]
    public async Task<IActionResult> Replay([FromQuery] Guid? after, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], auth.Value.SecretKey)) return Unauthorized();
        if (limit is < 1 or > 100) return BadRequest();
        var jobs = await reads.ReadyJobs().Where(j => after == null || j.Id.CompareTo(after.Value) > 0)
            .OrderBy(j => j.Id).Take(limit + 1).ToArrayAsync(ct);
        var page = jobs.Take(limit).ToArray();
        var ids = page.Select(j => j.Id).ToArray();
        var variants = await db.PhotoVariants.AsNoTracking().Where(v => ids.Contains(v.JobId)).ToArrayAsync(ct);
        var manifests = page.Select(j => PhotoVariantManifest.Create(j, variants.Where(v => v.JobId == j.Id))).ToArray();
        if (manifests.Any(m => !m.IsValid())) return StatusCode(503);
        outbox.Enroll(db);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var manifest in manifests) await outbox.PublishAsync(manifest);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        // Each new reconciliation pass starts without a cursor. Late commits behind this cursor
        // are picked up on the next pass; this is deliberately not a high-watermark delta stream.
        return Ok(new CheckInPhotoVariantReplayResult(page.Length, jobs.Length > limit ? page[^1].Id : null));
    }
}
