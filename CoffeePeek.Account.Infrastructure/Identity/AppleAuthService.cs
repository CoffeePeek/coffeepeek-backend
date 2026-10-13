using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CoffeePeek.Account.Infrastructure.Identity;

public sealed class AppleAuthService(
    IOptions<OAuthAppleOptions> options,
    IConfigurationManager<OpenIdConnectConfiguration> configuration) : IAppleAuthService
{
    public async Task<AppleIdentity?> ValidateIdTokenAsync(string idToken, string nonce, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 16384 || string.IsNullOrWhiteSpace(nonce))
            return null;
        var metadata = await configuration.GetConfigurationAsync(ct);
        var handler = new JsonWebTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = "https://appleid.apple.com",
            ValidateAudience = true, ValidAudience = options.Value.ClientId,
            RequireSignedTokens = true, ValidateIssuerSigningKey = true,
            IssuerSigningKeys = metadata.SigningKeys,
            ValidateLifetime = true, RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };
        var result = await handler.ValidateTokenAsync(idToken, parameters).WaitAsync(ct);
        if (!result.IsValid)
        {
            // Refresh discovery on key rotation; the next attempt uses fresh keys.
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
                configuration.RequestRefresh();
            return null;
        }
        var claims = result.ClaimsIdentity;
        var subject = claims.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject) || claims.FindFirst("nonce")?.Value != nonce)
            return null;
        return new AppleIdentity(subject, claims.FindFirst("email")?.Value,
            bool.TryParse(claims.FindFirst("email_verified")?.Value, out var verified) && verified);
    }
}
