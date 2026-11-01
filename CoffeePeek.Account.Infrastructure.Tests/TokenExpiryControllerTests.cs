using System.Globalization;
using CoffeePeek.Account.Application.Features.Auth.RefreshToken;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.AccountService.Controllers.Contracts;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wolverine;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class TokenExpiryControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_UsesActualExpiryNearAbsoluteSessionDeadline(bool native)
    {
        var expiresAt = DateTime.UtcNow.AddDays(1);
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.InvokeAsync<Response<RefreshTokenResponse>>(It.IsAny<object>(), default, null))
            .ReturnsAsync(Response<RefreshTokenResponse>.Success(new("access", "new-refresh",
                DateTime.UtcNow.AddMinutes(15), expiresAt)));
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "refreshToken=old-refresh";
        var controller = new TokensController(bus.Object, Mock.Of<IUserContext>())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var result = native ? await controller.RefreshNative(new("old-refresh")) : await controller.Refresh();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        if (native)
        {
            ok.Value.Should().BeOfType<Response<NativeTokenResponse>>().Which.Data.RefreshTokenExpiresAt
                .Should().Be(expiresAt);
        }
        else
        {
            context.Response.Headers.SetCookie.Should().ContainSingle().Which.Should()
                .Contain("expires=" + expiresAt.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public async Task BrowserRefresh_WhenExpired_ClearsOnlyThisClientsCookie()
    {
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.InvokeAsync<Response<RefreshTokenResponse>>(It.IsAny<object>(), default, null))
            .ReturnsAsync(Response<RefreshTokenResponse>.Error(System.Net.HttpStatusCode.Unauthorized,
                RefreshTokenHandler.TokenExpiredMessage));
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "refreshToken=expired";
        var controller = new TokensController(bus.Object, Mock.Of<IUserContext>())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var result = await controller.Refresh();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
        context.Response.Headers.SetCookie.Should().ContainSingle().Which.Should().StartWith("refreshToken=;");
    }
}
