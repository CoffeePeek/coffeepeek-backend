using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.RefreshToken;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.RefreshToken;

public class RefreshTokenHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IJWTTokenService> _jwtMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IRevokedUserStore> _revokedUserStoreMock = new();
    private readonly IOptions<JWTOptions> _jwtOptions = Options.Create(new JWTOptions
    {
        SecretKey = "test-secret-key-must-be-32-chars!",
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 90,
        RefreshTokenAbsoluteLifetimeDays = 365
    });
    private readonly CancellationToken _ct = CancellationToken.None;

    private static DomainUser CreateUserWithSession(string tokenValue = "active_refresh_token")
    {
        var role = Role.Create("User");
        var user = DomainUser.Register("user@example.com", "testuser", "hash", role);
        user.AddSession(tokenValue, TimeSpan.FromDays(7), "Chrome", "127.0.0.1");
        return user;
    }

    [Fact]
    public async Task Handle_WithValidToken_ReturnsNewTokenPair()
    {
        var user = CreateUserWithSession("valid_refresh");
        _userRepoMock.Setup(r => r.GetByRefreshToken("valid_refresh", _ct)).ReturnsAsync(user);
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("new_refresh");
        _jwtMock.Setup(j => j.GenerateAccessToken(user)).Returns("new_access");

        var response = await RefreshTokenHandler.Handle(new RefreshTokenCommand("valid_refresh", "Android", "203.0.113.7"), _userRepoMock.Object, _jwtMock.Object, _jwtOptions, _revokedUserStoreMock.Object, _unitOfWorkMock.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data!.AccessToken.Should().Be("new_access");
        response.Data.RefreshToken.Should().Be("new_refresh");
        user.RefreshTokens.Should().ContainSingle(t => t.IsActive && t.DeviceName == "Android" && t.IpAddress == "203.0.113.7");
        response.Data.AccessTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(2));
        response.Data.RefreshTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(90), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Handle_WithValidToken_RevokesOldToken()
    {
        var user = CreateUserWithSession("old_refresh");
        _userRepoMock.Setup(r => r.GetByRefreshToken("old_refresh", _ct)).ReturnsAsync(user);
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("new");
        _jwtMock.Setup(j => j.GenerateAccessToken(It.IsAny<DomainUser>())).Returns("access");

        await RefreshTokenHandler.Handle(new RefreshTokenCommand("old_refresh"), _userRepoMock.Object, _jwtMock.Object, _jwtOptions, _revokedUserStoreMock.Object, _unitOfWorkMock.Object, _ct);

        user.RefreshTokens.Single(t => t.Token == "old_refresh").IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithValidToken_CallsSaveChanges()
    {
        var user = CreateUserWithSession("token");
        _userRepoMock.Setup(r => r.GetByRefreshToken("token", _ct)).ReturnsAsync(user);
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("new");
        _jwtMock.Setup(j => j.GenerateAccessToken(It.IsAny<DomainUser>())).Returns("access");

        await RefreshTokenHandler.Handle(new RefreshTokenCommand("token"), _userRepoMock.Object, _jwtMock.Object, _jwtOptions, _revokedUserStoreMock.Object, _unitOfWorkMock.Object, _ct);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRefreshTokenNotFound_ThrowsUnauthorizedException()
    {
        _userRepoMock.Setup(r => r.GetByRefreshToken(It.IsAny<string>(), _ct)).ReturnsAsync((DomainUser?)null);

        Func<Task> act = () => RefreshTokenHandler.Handle(new RefreshTokenCommand("unknown_token"), _userRepoMock.Object, _jwtMock.Object, _jwtOptions, _revokedUserStoreMock.Object, _unitOfWorkMock.Object, _ct);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid refresh token");
    }

    [Fact]
    public async Task Handle_WithReusedToken_CommitsRevocationInsteadOfThrowing()
    {
        // Regression: throwing rolled back the Wolverine transaction, so the attacker's chain survived.
        var user = CreateUserWithSession("active_token");
        user.RefreshTokens.First().Revoke();
        user.AddSession("another_active", TimeSpan.FromDays(7), "mobile", "10.0.0.1");
        _userRepoMock.Setup(r => r.GetByRefreshToken("active_token", _ct)).ReturnsAsync(user);
        _revokedUserStoreMock
            .Setup(x => x.RevokeAsync(user.Id, TimeSpan.FromMinutes(15), _ct))
            .ReturnsAsync(true);

        var response = await RefreshTokenHandler.Handle(new RefreshTokenCommand("active_token"), _userRepoMock.Object, _jwtMock.Object, _jwtOptions, _revokedUserStoreMock.Object, _unitOfWorkMock.Object, _ct);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(401);
        user.RefreshTokens.All(t => !t.IsActive).Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _revokedUserStoreMock.Verify(x => x.RevokeAsync(user.Id, TimeSpan.FromMinutes(15), _ct), Times.Once);
        _jwtMock.Verify(j => j.GenerateAccessToken(It.IsAny<DomainUser>()), Times.Never);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handle_WithExpiredToken_PreservesOtherDevices(bool absoluteExpiry, bool revoked)
    {
        var role = Role.Create("User");
        var user = DomainUser.Register("user@example.com", "testuser", "hash", role);
        var expired = user.AddSession("expired_token", absoluteExpiry ? TimeSpan.FromDays(90) : TimeSpan.FromDays(-1),
            "Chrome", "127.0.0.1", absoluteLifetime: absoluteExpiry ? TimeSpan.FromDays(-1) : TimeSpan.FromDays(365));
        if (revoked) expired.Revoke();
        var otherDevice = user.AddSession("other-device", TimeSpan.FromDays(90), "Android", "203.0.113.7");
        _userRepoMock.Setup(r => r.GetByRefreshToken("expired_token", _ct)).ReturnsAsync(user);
        _revokedUserStoreMock
            .Setup(x => x.RevokeAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>(), _ct))
            .ReturnsAsync(true);

        var response = await RefreshTokenHandler.Handle(new RefreshTokenCommand("expired_token"), _userRepoMock.Object, _jwtMock.Object, _jwtOptions, _revokedUserStoreMock.Object, _unitOfWorkMock.Object, _ct);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(401);
        response.Message.Should().Be(RefreshTokenHandler.TokenExpiredMessage);
        otherDevice.IsActive.Should().BeTrue();
        user.RefreshTokens.Should().HaveCount(2);
        _revokedUserStoreMock.Verify(x => x.RevokeAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
        _jwtMock.Verify(j => j.GenerateAccessToken(It.IsAny<DomainUser>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NearAbsoluteDeadline_ReturnsActualRefreshExpiry()
    {
        var user = CreateUserWithSession("other-device");
        var token = user.AddSession("near-limit", TimeSpan.FromDays(90), "Android", "203.0.113.7",
            absoluteLifetime: TimeSpan.FromDays(1));
        _userRepoMock.Setup(r => r.GetByRefreshToken("near-limit", _ct)).ReturnsAsync(user);
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("new-refresh");
        _jwtMock.Setup(j => j.GenerateAccessToken(user)).Returns("new-access");

        var response = await RefreshTokenHandler.Handle(new("near-limit"), _userRepoMock.Object, _jwtMock.Object,
            _jwtOptions, _revokedUserStoreMock.Object, _unitOfWorkMock.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data.RefreshTokenExpiresAt.Should().Be(token.SessionExpiresAtUtc);
        user.RefreshTokens.Single(t => t.Token == "new-refresh").SessionExpiresAtUtc.Should().Be(token.SessionExpiresAtUtc);
    }
}
