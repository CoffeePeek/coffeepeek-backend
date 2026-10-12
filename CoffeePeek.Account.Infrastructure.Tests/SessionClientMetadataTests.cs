using System.Net;
using CoffeePeek.Account.Application.Features.Admin.Users.Sessions;
using CoffeePeek.Account.Application.Features.Auth.Login;
using CoffeePeek.Account.Application.Features.Auth.OAuthLogin;
using CoffeePeek.Account.Application.Features.Auth.RefreshToken;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.AccountService.Controllers.Contracts;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Web.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsOptions = Microsoft.Extensions.Options.Options;
using Moq;
using Wolverine;

namespace CoffeePeek.Account.Infrastructure.Tests;

public class SessionClientMetadataTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("google")]
    [InlineData("native")]
    [InlineData("refresh")]
    [InlineData("native-refresh")]
    public async Task TokenOperations_UseVerifiedIpAndOriginalUserAgent(string operation)
    {
        const string device = "CoffeePeek/1.0 (Android 16)";
        const string clientIp = "203.0.113.7";
        var bus = new Mock<IMessageBus>();
        var expires = DateTime.UtcNow.AddMinutes(15);
        bus.Setup(b => b.InvokeAsync<Response<LoginResponse>>(It.IsAny<object>(), default, null))
            .ReturnsAsync(Response<LoginResponse>.Success(new("access", "refresh", expires)));
        bus.Setup(b => b.InvokeAsync<Response<GoogleLoginResponse>>(It.IsAny<object>(), default, null))
            .ReturnsAsync(Response<GoogleLoginResponse>.Success(new("access", "refresh", expires,
                new GoogleLoginUser { Email = "user@example.com", Username = "user", AvatarUrl = "" })));
        bus.Setup(b => b.InvokeAsync<Response<RefreshTokenResponse>>(It.IsAny<object>(), default, null))
            .ReturnsAsync(Response<RefreshTokenResponse>.Success(new("access", "refresh", expires)));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "172.30.42.3"
        }).Build();
        using var services = new ServiceCollection().AddLogging()
            .AddTrustedProxyForwarding(configuration).BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseForwardedHeaders();
        app.Run(async context =>
        {
            var controller = new TokensController(bus.Object, Mock.Of<IUserContext>(), MsOptions.Create(new JWTOptions()))
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };
            var result = operation switch
            {
                "password" => await controller.Create(new("user@example.com", "password", "spoof", "1.2.3.4")),
                "google" => await controller.GoogleLogin(new("id-token", "spoof", "1.2.3.4")),
                "native" => await controller.CreateNative(new("user@example.com", "password", "spoof", "1.2.3.4")),
                "refresh" => await controller.Refresh(),
                _ => await controller.RefreshNative(new NativeRefreshTokenRequest("old-refresh"))
            };
            result.Should().BeOfType<OkObjectResult>();
        });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.30.42.3");
        context.Request.Headers["X-Forwarded-For"] = "1.2.3.4, " + clientIp;
        context.Request.Headers.UserAgent = device;
        context.Request.Headers.Cookie = "refreshToken=old-refresh";
        await app.Build()(context);

        var command = bus.Invocations.Should().ContainSingle().Subject.Arguments[0];
        var metadata = command switch
        {
            LoginUserCommand login => (login.DeviceName, login.IpAddress),
            GoogleLoginCommand google => (google.DeviceName, google.IpAddress),
            RefreshTokenCommand refresh => (refresh.DeviceName, refresh.IpAddress),
            _ => throw new InvalidOperationException("Unexpected token command")
        };
        metadata.Should().Be((device, clientIp));
    }

    [Fact]
    public async Task AdminSessions_ReturnStoredIpAndDeviceWithoutRefreshToken()
    {
        var user = User.CreateExternal("user@example.com", "Google", "subject");
        user.AddSession("private-token", TimeSpan.FromDays(7), "CoffeePeek/1.0 (Android)", "203.0.113.7");
        var repository = new Mock<IUserRepository>();
        repository.Setup(r => r.GetById(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.InvokeAsync<Response<GetAdminUserSessionsResponse>>(
                It.IsAny<object>(), It.IsAny<CancellationToken>(), null))
            .Returns(async (object query, CancellationToken ct, TimeSpan? _) =>
                await GetAdminUserSessionsHandler.Handle((GetAdminUserSessionsQuery)query, repository.Object, ct));

        var result = await new AdminUsersController(bus.Object, Mock.Of<IUserContext>())
            .GetSessions(user.Id, TestContext.Current.CancellationToken);
        var response = result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Response<GetAdminUserSessionsResponse>>().Subject;
        var session = response.Data.Sessions.Should().ContainSingle().Subject;
        session.IpAddress.Should().Be("203.0.113.7");
        session.DeviceName.Should().Be("CoffeePeek/1.0 (Android)");
        System.Text.Json.JsonSerializer.Serialize(response).Should().NotContain("private-token");
    }
}
