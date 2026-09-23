using System.Security.Claims;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace CoffeePeek.ModerationService.Tests;

public class HeaderUserContextTests
{
    [Fact]
    public void RawIdentityHeaders_WithoutAuthenticatedPrincipal_AreIgnored()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[GatewayHeaderConsts.XUserId] = Guid.NewGuid().ToString();
        http.Request.Headers[GatewayHeaderConsts.XUserName] = "spoofed";
        http.Request.Headers[GatewayHeaderConsts.XUserRole] = "Admin";

        var context = new HeaderUserContext(new HttpContextAccessor { HttpContext = http });

        context.GetUserId().Should().BeNull();
        context.GetUsername().Should().BeNull();
        context.HasAnyRole("Admin").Should().BeFalse();
    }

    [Fact]
    public void Claims_AreReturned()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "alice"),
                new Claim(ClaimTypes.Role, "User"),
                new Claim(ClaimTypes.Role, "Moderator")
            ], "test"))
        };

        var context = new HeaderUserContext(new HttpContextAccessor { HttpContext = http });

        context.GetUserId().Should().Be(userId);
        context.GetUsername().Should().Be("alice");
        context.HasAnyRole("Moderator").Should().BeTrue();
    }
}
