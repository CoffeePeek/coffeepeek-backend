using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;
using Wolverine;
using CoffeePeek.AccountService.Controllers.Contracts;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/Tokens/apple")]
[Tags("Tokens management")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AppleWebTokensController(IMessageBus bus, IOptions<JWTOptions> jwtOptions) : ControllerBase
{
    private const string AppleStateCookie = "__Host-cp-apple-state";

    /// <summary>Start browser sign-in with Apple.</summary>
    [HttpGet("authorize")]
    public async Task<IActionResult> AppleAuthorize(
        [FromServices] IAppleWebAuthService appleWebAuth,
        [FromServices] IConfiguration configuration,
        CancellationToken ct)
    {
        GetAppleWebClientUrl(configuration);
        var authorization = await appleWebAuth.CreateAuthorizationAsync(ct);
        var cookie = AppleStateCookieOptions();
        cookie.Expires = authorization.ExpiresAt;
        Response.Cookies.Append(AppleStateCookie, authorization.State, cookie);
        return Redirect(authorization.AuthorizationUrl);
    }

    /// <summary>Receive Apple's form POST, establish a CoffeePeek session and return to the website.</summary>
    [HttpPost("callback")]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(16384)]
    [RequestFormLimits(ValueLengthLimit = 4096, ValueCountLimit = 10)]
    public async Task<IActionResult> AppleCallback(
        [FromForm] AppleCallbackRequest request,
        [FromServices] IAppleWebAuthService appleWebAuth,
        [FromServices] IConfiguration configuration,
        CancellationToken ct)
    {
        var webClientUrl = GetAppleWebClientUrl(configuration);
        if (!Request.Cookies.TryGetValue(AppleStateCookie, out var cookieState) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookieState), Encoding.UTF8.GetBytes(request.State)))
            return Unauthorized(new { message = "Invalid Apple sign-in state. Please restart sign-in." });

        Response.Cookies.Delete(AppleStateCookie, AppleStateCookieOptions());
        var command = await appleWebAuth.ExchangeCodeAsync(request.Error is null ? request.Code : null, request.State, ct);
        if (command is null)
            return Unauthorized(new { message = "Apple sign-in failed or expired. Please restart sign-in." });

        var (device, ip) = HttpContext.SessionClient();
        var response = await bus.InvokeAsync<Response<AppleLoginResponse>>(command with { DeviceName = device, IpAddress = ip }, ct);
        if (response is not { IsSuccess: true, Data: not null })
            return StatusCode(response.StatusCode ?? StatusCodes.Status401Unauthorized, response);

        Response.SetRefreshCookie(response.Data.RefreshToken, jwtOptions.Value);
        return Redirect(webClientUrl);
    }

    private static CookieOptions AppleStateCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None, // Apple's form_post callback is a cross-site POST.
        IsEssential = true,
        Path = "/"
    };

    private static string GetAppleWebClientUrl(IConfiguration configuration)
    {
        var value = configuration["WebClientUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ServiceUnavailableException("Apple web sign-in return URL is not configured.");
        return value!;
    }
}
