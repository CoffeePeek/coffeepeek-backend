using System.Security.Claims;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Gateway.Tests;

public class ClaimsTransformationExtensionsTests
{
    [Theory]
    [InlineData("alice")]
    [InlineData("Аліса")]
    public async Task Username_RoundTripsThroughGatewayHeader(string userName)
    {
        const string scheme = "Header";
        const string secret = "test-gateway-secret-that-is-long-enough";
        var userId = Guid.NewGuid().ToString();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName)
        ], "test"));

        var headers = ClaimsTransformationExtensions.ExtractClaimsAsHeaders(principal);

        headers[GatewayHeaderConsts.XUserName].All(char.IsAscii).Should().BeTrue();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<GatewayAuthOptions>(options => options.SecretKey = secret);
        services.AddAuthentication(scheme)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(scheme, null);

        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = secret;
        context.Request.Headers[GatewayHeaderConsts.XUserId] = userId;
        context.Request.Headers[GatewayHeaderConsts.XUserName] = headers[GatewayHeaderConsts.XUserName];

        var result = await context.AuthenticateAsync(scheme);

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.Name.Should().Be(userName);
    }
}
