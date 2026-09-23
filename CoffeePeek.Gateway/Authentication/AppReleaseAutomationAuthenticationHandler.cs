using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CoffeePeek.Shared.Auth.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Gateway.Authentication;

public sealed class AppReleaseAutomationAuthenticationHandler(
    IOptionsMonitor<AppReleaseAutomationOptions> automationOptions,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var rawHeader = Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(rawHeader, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var currentOptions = automationOptions.CurrentValue;
        if (!AppReleaseAutomationTokenValidator.IsValid(currentOptions.Token, header.Parameter))
            return Task.FromResult(AuthenticateResult.Fail("Invalid app release automation token."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, currentOptions.ActorUserId.ToString()),
            new Claim(ClaimTypes.Name, "GitHub Actions"),
            new Claim(ClaimTypes.Role, RoleConsts.Admin),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
