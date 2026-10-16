using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/Tokens/apple")]
[Tags("Tokens management")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AppleTokensController(IAppleLoginChallengeStore challenges, IMessageBus bus) : ControllerBase
{
    [HttpPost("challenge")]
    public async Task<ActionResult<Response<AppleLoginChallenge>>> Challenge(CancellationToken ct) =>
        Ok(Response<AppleLoginChallenge>.Success(await challenges.CreateAsync(ct)));

    [HttpPost("login")]
    [HttpPost("native")]
    public async Task<IActionResult> Login(AppleLoginCommand request, CancellationToken ct)
    {
        var command = request with
        {
            DeviceName = Request.Headers.UserAgent.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };
        var response = await bus.InvokeAsync<Response<AppleLoginResponse>>(command, ct);
        return StatusCode(response.IsSuccess ? StatusCodes.Status200OK : response.StatusCode ?? StatusCodes.Status401Unauthorized, response);
    }
}
