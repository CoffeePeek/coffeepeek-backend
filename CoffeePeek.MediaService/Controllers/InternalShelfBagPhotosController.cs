using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Controllers;

/// <summary>Shops-only shelf-bag card claim and protected read; never routed by the gateway.</summary>
[ApiController]
[Route("internal/shelf-bag-photos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InternalShelfBagPhotosController(ClaimShelfBagPhotoHandler claims, IPhotoRepository repository,
    ICheckInPhotoReader reader, IUserContext userContext, IOptions<GatewayAuthOptions> authOptions) : ControllerBase
{
    private bool IsTrusted => GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth],
        authOptions.Value.SecretKey);

    [HttpPost("claim")]
    public async Task<IActionResult> Claim([FromBody] ClaimShelfBagPhotoRequest request, CancellationToken ct)
    {
        if (!IsTrusted || userContext.GetUserId() is not { } ownerId) return Unauthorized();
        var response = await claims.ClaimAsync(request, ownerId, ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? 400, response);
    }

    [HttpGet("{bagId:guid}/{photoId:guid}")]
    public async Task<IActionResult> Download(Guid bagId, Guid photoId, CancellationToken ct)
    {
        if (!IsTrusted || userContext.GetUserId() is not { } ownerId) return Unauthorized();
        var photo = await repository.GetShelfBagPhotoAsync(bagId, photoId, ownerId, ct);
        if (photo == null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.ContentType = ShelfBagPhotoValidation.IsAllowedContentType(photo.ContentType)
            ? photo.ContentType.ToLowerInvariant() : "application/octet-stream";
        await reader.CopyToAsync(photo.StorageKey, Response.Body, ct);
        return new EmptyResult();
    }
}
