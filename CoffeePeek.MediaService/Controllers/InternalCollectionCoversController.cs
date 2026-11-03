using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Controllers;

/// <summary>Shops-only collection cover claim and protected read; never routed by the gateway.</summary>
[ApiController]
[Route("internal/collection-covers")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InternalCollectionCoversController(ClaimCollectionCoverHandler claims, IPhotoRepository repository,
    ICheckInPhotoReader reader, IUserContext userContext, IOptions<GatewayAuthOptions> authOptions) : ControllerBase
{
    private bool IsTrusted => GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth],
        authOptions.Value.SecretKey);

    [HttpPost("claim")]
    public async Task<IActionResult> Claim([FromBody] ClaimCollectionCoverRequest request, CancellationToken ct)
    {
        if (!IsTrusted || userContext.GetUserId() is not { } ownerId) return Unauthorized();
        var response = await claims.ClaimAsync(request, ownerId, ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? 400, response);
    }

    [HttpGet("{collectionId:guid}/{photoId:guid}")]
    public async Task<IActionResult> Download(Guid collectionId, Guid photoId, CancellationToken ct)
    {
        if (!IsTrusted || userContext.GetUserId() is not { } ownerId) return Unauthorized();
        var photo = await repository.GetCollectionCoverAsync(collectionId, photoId, ownerId, ct);
        if (photo == null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.ContentType = PrivateCardPhotoValidation.IsAllowedContentType(photo.ContentType)
            ? photo.ContentType.ToLowerInvariant() : "application/octet-stream";
        await reader.CopyToAsync(photo.StorageKey, Response.Body, ct);
        return new EmptyResult();
    }
}
