using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[Route("internal/shop-photos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalShopPhotosController(ClaimShopPhotosHandler claims, IUserContext userContext,
    IOptions<GatewayAuthOptions> authOptions) : ControllerBase
{
    [HttpPost("claim")]
    public async Task<IActionResult> Claim([FromBody] ClaimShopPhotosRequest request, CancellationToken ct)
    {
        if (!GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth],
                authOptions.Value.SecretKey) || userContext.GetUserId() is not { } ownerId)
            return Unauthorized();

        var result = await claims.ClaimAsync(request, ownerId, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
