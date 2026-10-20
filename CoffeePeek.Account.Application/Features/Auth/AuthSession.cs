using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Shared.Auth.Options;

namespace CoffeePeek.Account.Application.Features.Auth;

/// <summary>Issues an access token and a new refresh session. The caller's transaction persists the session.</summary>
internal static class AuthSession
{
    public static (string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt) Issue(
        Domain.Entities.UserAggregate.User user, IJWTTokenService tokens, JWTOptions options, string device, string ip)
    {
        user.EnsureCanAuthenticate();
        var accessToken = tokens.GenerateAccessToken(user);
        var refreshToken = tokens.GenerateRefreshToken();
        user.AddSession(refreshToken, TimeSpan.FromDays(options.RefreshTokenLifetimeDays), device, ip);
        return (accessToken, refreshToken, DateTime.UtcNow.AddMinutes(options.AccessTokenLifetimeMinutes));
    }
}
