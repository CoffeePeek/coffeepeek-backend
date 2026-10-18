using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/users/{viewerId:guid}/excluded-authors")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalAuthorBlocksController(IUserBlockStore blocks, IOptions<GatewayAuthOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid viewerId, CancellationToken ct) =>
        GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], options.Value.SecretKey)
            ? Ok(await blocks.ExcludedUserIdsAsync(viewerId, ct)) : Unauthorized();
}
