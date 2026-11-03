using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Application.Features.Discovery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Media-only reconciliation authority; no Gateway route exposes this endpoint.</summary>
[ApiController, Route("internal/collection-covers")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalCollectionCoverReferencesController(ISavedCollectionsStore collections, IUserContext user,
    IOptions<GatewayAuthOptions> auth) : ControllerBase
{
    [HttpGet("{collectionId:guid}/{photoId:guid}")]
    public async Task<IActionResult> Get(Guid collectionId, Guid photoId, CancellationToken ct)
    {
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], auth.Value.SecretKey)
            || user.GetUserId() is not { } owner) return Unauthorized();
        return await collections.CoverAsync(owner, collectionId, photoId, ct) == null ? NotFound() : NoContent();
    }
}
