using System.Security.Claims;
using System.Text.Encodings.Web;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shared.Auth;

public class HeaderAuthenticationHandler(
    IOptionsMonitor<GatewayAuthOptions> gatewayAuthOptions,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Context.Request.Headers[GatewayHeaderConsts.XUserId].ToString();

        if (string.IsNullOrEmpty(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var gatewayAuthHeader = Context.Request.Headers[GatewayHeaderConsts.XGatewayAuth].ToString();
        if (!GatewayAuthVerifier.IsTrusted(gatewayAuthHeader, gatewayAuthOptions.CurrentValue.SecretKey))
            return Task.FromResult(AuthenticateResult.Fail("Request identity headers are not trusted outside the gateway."));

        var roles = Context.Request.Headers[GatewayHeaderConsts.XUserRole].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var userName = Context.Request.Headers[GatewayHeaderConsts.XUserName].ToString();
        if (!string.IsNullOrEmpty(userName))
            claims.Add(new Claim(ClaimTypes.Name, Uri.UnescapeDataString(userName)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
