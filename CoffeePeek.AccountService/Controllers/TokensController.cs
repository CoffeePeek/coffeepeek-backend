using CoffeePeek.Account.Application.Features.Auth.Login;
using CoffeePeek.Account.Application.Features.Auth.Logout;
using CoffeePeek.Account.Application.Features.Auth.OAuthLogin;
using CoffeePeek.Account.Application.Features.Auth.RefreshToken;
using CoffeePeek.AccountService.Controllers.Contracts;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wolverine;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Tokens management")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class TokensController(
    IMessageBus bus,
    IUserContext userContext,
    IOptions<JWTOptions> jwtOptions) : ControllerBase
{
    /// <summary>
    /// Login user and get token
    /// </summary>
    [HttpPost]
    [ProducesResponseType<Response<LoginResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] LoginUserCommand request)
    {
        var (device, ip) = HttpContext.SessionClient();
        var response = await bus.InvokeAsync<Response<LoginResponse>>(request with { DeviceName = device, IpAddress = ip });

        Response.SetRefreshCookie(response.Data.RefreshToken, jwtOptions.Value);

        return Ok(response);
    }

    /// <summary>
    /// OAuth login with google
    /// </summary>
    [HttpPost("google/login")]
    [ProducesResponseType<Response<GoogleLoginResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginCommand request)
    {
        var (device, ip) = HttpContext.SessionClient();
        var response = await bus.InvokeAsync<Response<GoogleLoginResponse>>(request with { DeviceName = device, IpAddress = ip });

        if (!response.IsSuccess)
            return StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);

        if (response.Data is not null)
            Response.SetRefreshCookie(response.Data.RefreshToken, jwtOptions.Value);
        return Ok(response);
    }

    /// <summary>
    /// Login for native clients. Returns the refresh token in JSON instead of a browser cookie.
    /// </summary>
    [HttpPost("native")]
    [ProducesResponseType<Response<NativeTokenResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateNative([FromBody] LoginUserCommand request)
    {
        var (device, ip) = HttpContext.SessionClient();
        var response = await bus.InvokeAsync<Response<LoginResponse>>(request with { DeviceName = device, IpAddress = ip });

        return Ok(ToNativeResponse(response.Data.AccessToken, response.Data.RefreshToken, response.Data.AccessTokenExpiresAt));
    }

    /// <summary>
    /// Rotate a refresh token for native clients.
    /// </summary>
    [HttpPut("native")]
    [AllowAnonymous]
    [ProducesResponseType<Response<NativeTokenResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshNative([FromBody] NativeRefreshTokenRequest request)
    {
        var (device, ip) = HttpContext.SessionClient();
        var response = await bus.InvokeAsync<Response<RefreshTokenResponse>>(new RefreshTokenCommand(request.RefreshToken, device, ip));
        if (response is not { IsSuccess: true, Data: not null })
            return StatusCode(response.StatusCode ?? StatusCodes.Status401Unauthorized, response);

        return Ok(ToNativeResponse(response.Data.AccessToken, response.Data.RefreshToken, response.Data.AccessTokenExpiresAt));
    }

    /// <summary>
    /// Refresh token from cookies. Does not require a valid access token —
    /// the user is resolved from the refresh token value itself.
    /// </summary>
    [HttpPut]
    [AllowAnonymous]
    [ProducesResponseType<Response<RefreshTokenResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.TryGetRefreshCookie(out var refreshToken))
            return BadRequest(new { message = "Refresh token is required" });

        var (device, ip) = HttpContext.SessionClient();
        try
        {
            var response = await bus.InvokeAsync<Response<RefreshTokenResponse>>(new RefreshTokenCommand(refreshToken, device, ip));

            if (response is not { IsSuccess: true, Data: not null })
            {
                // Token reuse: the handler returns (not throws) so the session revocation is committed.
                Response.DeleteRefreshCookie();
                return StatusCode(response.StatusCode ?? StatusCodes.Status401Unauthorized, response);
            }

            Response.SetRefreshCookie(response.Data.RefreshToken, jwtOptions.Value);
            return Ok(response);
        }
        catch (UnauthorizedException)
        {
            Response.DeleteRefreshCookie();
            throw;
        }
    }

    /// <summary>
    /// Logout user and invalidate refresh token from cookies
    /// </summary>
    [HttpDelete]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete()
    {
        if (!Request.TryGetRefreshCookie(out var refreshToken))
            return BadRequest(new { message = "Refresh token is required" });

        await bus.InvokeAsync(new LogoutCommand(userContext.GetUserIdOrThrow(), refreshToken));

        Response.DeleteRefreshCookie();
        return NoContent();
    }

    /// <summary>
    /// Logout a native client using the refresh token that identifies its session.
    /// </summary>
    [HttpDelete("native")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteNative([FromBody] NativeLogoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { message = "Refresh token is required" });

        await bus.InvokeAsync(new LogoutByRefreshTokenCommand(request.RefreshToken));

        return NoContent();
    }

    private Response<NativeTokenResponse> ToNativeResponse(string accessToken, string refreshToken, DateTime accessTokenExpiresAt) =>
        Response<NativeTokenResponse>.Success(new NativeTokenResponse(accessToken, refreshToken, accessTokenExpiresAt,
            DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenLifetimeDays)));
}
