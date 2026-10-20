using CoffeePeek.Shared.Auth.Options;

namespace CoffeePeek.AccountService.Controllers;

/// <summary>Refresh-token cookie rules and session client metadata shared by the token endpoints.</summary>
internal static class TokenHttp
{
    private const string RefreshCookie = "refreshToken";

    public static bool TryGetRefreshCookie(this HttpRequest request, out string token) =>
        request.Cookies.TryGetValue(RefreshCookie, out token!);

    public static void SetRefreshCookie(this HttpResponse response, string token, JWTOptions jwt) =>
        response.Cookies.Append(RefreshCookie, token,
            RefreshCookieOptions(DateTimeOffset.UtcNow.AddDays(jwt.RefreshTokenLifetimeDays)));

    public static void DeleteRefreshCookie(this HttpResponse response) =>
        response.Cookies.Delete(RefreshCookie, RefreshCookieOptions(null));

    /// <summary>The original user agent and the proxy-verified client IP recorded on the session.</summary>
    public static (string Device, string Ip) SessionClient(this HttpContext context) =>
        (context.Request.Headers.UserAgent.ToString(), context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static CookieOptions RefreshCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/tokens",
        Expires = expires
    };
}
