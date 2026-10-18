using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Media;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[Route("internal/check-in-photo-variants")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalCheckInPhotoVariantsController(PhotoVariantReads reads, PhotoVariantReadState state,
    IOptions<PhotoVariantOptions> options, IUserContext user, IOptions<GatewayAuthOptions> auth) : ControllerBase
{
    [HttpGet("{checkInId:guid}/{photoId:guid}/{source}/{recipe:int}/{purpose}.{format}")]
    [HttpHead("{checkInId:guid}/{photoId:guid}/{source}/{recipe:int}/{purpose}.{format}")]
    public async Task<IActionResult> Download(Guid checkInId, Guid photoId, string source, int recipe,
        string purpose, string format, [FromServices] IServiceProvider services, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], auth.Value.SecretKey)
            || user.GetUserId() is not { } owner) return Unauthorized();
        if (!options.Value.ReadEnabled || !CheckInPhotoVariantIdentity.IsAddress(source, recipe, purpose, format)) return NotFound();
        var variant = await reads.FindAsync(owner, checkInId, photoId, source, recipe, purpose, format, ct);
        if (variant is null) return NotFound();
        state.Lease.RequireReady(); // Includes HEAD and 304; an expired policy lease fails closed.
        var etag = CheckInPhotoVariantIdentity.ETag(source, recipe, variant.Digest);
        Response.Headers.ETag = etag;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.ContentType = variant.ContentType;
        Response.ContentLength = variant.SizeBytes;
        if (EntityTagHeaderValue.TryParseList(Request.Headers.IfNoneMatch.Select(value => value ?? "").ToArray(), out var tags)
            && tags.Any(t => t == EntityTagHeaderValue.Any || t.Compare(new EntityTagHeaderValue(etag), useStrongComparison: false)))
            return StatusCode(StatusCodes.Status304NotModified);
        if (!HttpMethods.IsHead(Request.Method))
            await services.GetRequiredService<IPhotoVariantReader>().CopyToAsync(variant.ObjectKey, Response.Body, ct);
        return new EmptyResult();
    }
}
