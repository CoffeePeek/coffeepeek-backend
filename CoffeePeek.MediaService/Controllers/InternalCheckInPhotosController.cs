using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[Route("internal/check-in-photos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InternalCheckInPhotosController(ClaimCheckInPhotosHandler claims, CopyShelfBagCardHandler cardCopies, IPhotoRepository repository,
    ICheckInPhotoReader reader, IUserContext userContext, IOptions<GatewayAuthOptions> authOptions) : ControllerBase
{
    private bool IsTrusted => GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth],
        authOptions.Value.SecretKey);

    [HttpPost("claim")]
    public async Task<IActionResult> Claim([FromBody] ClaimCheckInPhotosRequest request, CancellationToken ct)
    {
        if (!IsTrusted || userContext.GetUserId() is not { } ownerId) return Unauthorized();
        var response = await claims.ClaimAsync(request, ownerId, ct);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpPost("shelf-bag-card")]
    public async Task<IActionResult> CopyShelfBagCard([FromBody] CopyShelfBagCardRequest request, CancellationToken ct)
    {
        if (!IsTrusted || userContext.GetUserId() is not { } ownerId) return Unauthorized();
        var response = await cardCopies.CopyAsync(request, ownerId, ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? 400, response);
    }

    [HttpDelete("{checkInId:guid}/claim")]
    public async Task<IActionResult> Release(Guid checkInId, CancellationToken ct)
    {
        if (!IsTrusted || userContext.GetUserId() is not { } ownerId) return Unauthorized();
        await repository.ReleaseClaimAsync(ownerId, checkInId, ct);
        return NoContent();
    }

    [HttpGet("{checkInId:guid}/{photoId:guid}")]
    public async Task<IActionResult> Download(Guid checkInId, Guid photoId, CancellationToken ct)
    {
        if (!IsTrusted || userContext.GetUserId() is not { } ownerId) return Unauthorized();
        var photo = await repository.GetCheckInPhotoAsync(checkInId, photoId, ownerId, ct);
        if (photo == null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (GenerateShopPhotoHandler.IsSafePhotoContentType(photo.ContentType))
            Response.ContentType = photo.ContentType.ToLowerInvariant();
        else
        {
            Response.ContentType = "application/octet-stream";
            Response.Headers.ContentDisposition = "attachment";
        }
        await reader.CopyToAsync(photo.StorageKey, Response.Body, ct);
        return new EmptyResult();
    }
}
