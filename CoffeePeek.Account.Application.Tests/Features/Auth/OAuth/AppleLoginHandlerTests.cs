using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.OAuth;

public sealed class AppleLoginHandlerTests
{
    private readonly Mock<IAppleLoginChallengeStore> challenges = new();
    private readonly Mock<IAppleAuthService> apple = new();
    private readonly Mock<IUserRepository> users = new();
    private readonly Mock<IExternalAuthService> external = new();
    private readonly Mock<IJWTTokenService> tokens = new();
    private readonly Mock<IUnitOfWork> uow = new();
    private readonly Guid challengeId = Guid.NewGuid();
    private readonly DomainUser user = DomainUser.CreateExternal("relay@privaterelay.appleid.com", "AppleProvider", "apple-subject");

    public AppleLoginHandlerTests()
    {
        challenges.Setup(x => x.GetNonceAsync(challengeId, It.IsAny<CancellationToken>())).ReturnsAsync("nonce");
        challenges.Setup(x => x.ConsumeAsync(challengeId, "nonce", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        apple.Setup(x => x.ValidateIdTokenAsync("token", "nonce", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleIdentity("apple-subject", "relay@privaterelay.appleid.com", true));
        external.Setup(x => x.GetOrCreate("relay@privaterelay.appleid.com", "AppleProvider", "apple-subject", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        tokens.Setup(x => x.GenerateAccessToken(user)).Returns("access");
        tokens.Setup(x => x.GenerateRefreshToken()).Returns("refresh");
    }

    private Task<Response<AppleLoginResponse>> Handle() => AppleLoginHandler.Handle(
        new AppleLoginCommand(challengeId, "token", "iPhone", "127.0.0.1"), challenges.Object, apple.Object,
        users.Object, external.Object, tokens.Object, Options.Create(new JWTOptions { RefreshTokenLifetimeDays = 7, AccessTokenLifetimeMinutes = 15 }),
        uow.Object, CancellationToken.None);

    [Fact]
    public async Task VerifiedAppleAccount_CreatesCoffeePeekSession()
    {
        var response = await Handle();
        response.IsSuccess.Should().BeTrue();
        response.Data!.RefreshToken.Should().Be("refresh");
        user.RefreshTokens.Should().ContainSingle(x => x.IsActive && x.DeviceName == "iPhone");
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubsequentLoginWithoutEmail_UsesStableSubject()
    {
        apple.Setup(x => x.ValidateIdTokenAsync("token", "nonce", It.IsAny<CancellationToken>())).ReturnsAsync(new AppleIdentity("apple-subject", null, false));
        users.Setup(x => x.GetByProvider("AppleProvider", "apple-subject", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        (await Handle()).IsSuccess.Should().BeTrue();
        external.Verify(x => x.GetOrCreate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnverifiedEmail_CannotCreateAccount()
    {
        apple.Setup(x => x.ValidateIdTokenAsync("token", "nonce", It.IsAny<CancellationToken>())).ReturnsAsync(new AppleIdentity("apple-subject", "someone@example.com", false));
        (await Handle()).StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        external.Verify(x => x.GetOrCreate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExpiredChallenge_DoesNotValidateOrCreateSession()
    {
        challenges.Setup(x => x.GetNonceAsync(challengeId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        (await Handle()).StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        apple.Verify(x => x.ValidateIdTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConcurrentOrReplayedChallenge_CannotIssueSecondSession()
    {
        challenges.SetupSequence(x => x.ConsumeAsync(challengeId, "nonce", It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        (await Handle()).IsSuccess.Should().BeTrue();
        (await Handle()).StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidToken_DoesNotConsumeChallenge()
    {
        apple.Setup(x => x.ValidateIdTokenAsync("token", "nonce", It.IsAny<CancellationToken>())).ReturnsAsync((AppleIdentity?)null);
        (await Handle()).StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        challenges.Verify(x => x.ConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletedAccount_CannotAuthenticate()
    {
        user.SetSoftDelete();
        users.Setup(x => x.GetByProvider("AppleProvider", "apple-subject", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var action = () => Handle();
        await action.Should().ThrowAsync<Exception>();
        tokens.Verify(x => x.GenerateAccessToken(user), Times.Never);
    }
}
