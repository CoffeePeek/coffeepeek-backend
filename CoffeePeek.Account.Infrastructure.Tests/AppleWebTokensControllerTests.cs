using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Models;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.AccountService.Controllers.Contracts;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MsOptions = Microsoft.Extensions.Options.Options;
using Moq;
using Wolverine;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class AppleWebTokensControllerTests
{
    private const string State = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly Mock<IMessageBus> _bus = new();
    private readonly Mock<IAppleWebAuthService> _webAuth = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["WebClientUrl"] = "https://coffeepeek.by" }).Build();

    private AppleWebTokensController Controller(string? cookieState = null)
    {
        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;
        context.Request.Headers.UserAgent = "Browser";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        if (cookieState is not null) context.Request.Headers.Cookie = "__Host-cp-apple-state=" + cookieState;
        return new AppleWebTokensController(_bus.Object, MsOptions.Create(new JWTOptions { RefreshTokenLifetimeDays = 7 }))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    [Fact]
    public async Task Authorize_RedirectsWithProtectedCrossSiteCorrelationCookie()
    {
        const string url = "https://appleid.apple.com/auth/authorize?state=" + State;
        _webAuth.Setup(x => x.CreateAuthorizationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleAuthorizationRequest(url, State, DateTimeOffset.UtcNow.AddMinutes(5)));
        var controller = Controller();

        var result = await controller.AppleAuthorize(_webAuth.Object, _configuration, TestContext.Current.CancellationToken);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(url);
        var cookie = controller.Response.Headers.SetCookie.Single()!;
        cookie.Should().StartWith("__Host-cp-apple-state=" + State).And.Contain("path=/")
            .And.Contain("secure").And.Contain("httponly").And.Contain("samesite=none").And.NotContain("domain=");
    }

    [Fact]
    public async Task Callback_UsesExistingChallengeContractAndSetsExistingRefreshCookieRules()
    {
        var challengeId = Guid.NewGuid();
        _webAuth.Setup(x => x.ExchangeCodeAsync("apple-code", State, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleLoginCommand(challengeId, "apple-id-token"));
        _bus.Setup(x => x.InvokeAsync<Response<AppleLoginResponse>>(
                It.Is<object>(c => c is AppleLoginCommand && ((AppleLoginCommand)c).ChallengeId == challengeId &&
                    ((AppleLoginCommand)c).IdToken == "apple-id-token" && ((AppleLoginCommand)c).DeviceName == "Browser" &&
                    ((AppleLoginCommand)c).IpAddress == "192.0.2.1"), It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(Response<AppleLoginResponse>.Success(new AppleLoginResponse("access-secret", "refresh-secret", DateTime.UtcNow.AddMinutes(15))));
        var controller = Controller(State);

        var result = await controller.AppleCallback(new AppleCallbackRequest { State = State, Code = "apple-code" },
            _webAuth.Object, _configuration, TestContext.Current.CancellationToken);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("https://coffeepeek.by");
        var refreshCookie = controller.Response.Headers.SetCookie.Single(c => c!.StartsWith("refreshToken="))!;
        refreshCookie.Should().StartWith("refreshToken=refresh-secret").And.Contain("path=/api/tokens")
            .And.Contain("secure").And.Contain("httponly").And.Contain("samesite=strict").And.Contain("expires=");
        controller.Response.Headers.SetCookie.Should().Contain(c => c!.StartsWith("__Host-cp-apple-state=;") && c.Contains("expires="));
        _bus.VerifyAll();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("another-browser-state")]
    public async Task Callback_MissingOrMismatchedCookie_DoesNotExchangeCode(string? cookieState)
    {
        var controller = Controller(cookieState);
        var result = await controller.AppleCallback(new AppleCallbackRequest { State = State, Code = "apple-code" },
            _webAuth.Object, _configuration, TestContext.Current.CancellationToken);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _webAuth.Verify(x => x.ExchangeCodeAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _bus.Invocations.Should().BeEmpty();
        controller.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("access_denied")]
    public async Task Callback_FailedOrCancelledExchange_CannotIssueSession(string? error)
    {
        _webAuth.Setup(x => x.ExchangeCodeAsync(error == null ? "apple-code" : null, State, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppleLoginCommand?)null);
        var controller = Controller(State);

        var result = await controller.AppleCallback(new AppleCallbackRequest { State = State, Code = "apple-code", Error = error },
            _webAuth.Object, _configuration, TestContext.Current.CancellationToken);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _bus.Invocations.Should().BeEmpty();
        controller.Response.Headers.SetCookie.Should().NotContain(c => c!.StartsWith("refreshToken="));
        _webAuth.VerifyAll();
    }

    [Fact]
    public async Task Authorize_InvalidReturnUrl_DoesNotStartFlow()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["WebClientUrl"] = "http://coffeepeek.by" }).Build();
        var action = () => Controller().AppleAuthorize(_webAuth.Object, configuration, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<ServiceUnavailableException>();
        _webAuth.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void CallbackLogging_RedactsStateAndCode()
    {
        var request = new AppleCallbackRequest { State = State, Code = "apple-code", Error = "sensitive-error" };
        request.ToString().Should().NotContain(State).And.NotContain("apple-code").And.NotContain("sensitive-error");
    }
}
