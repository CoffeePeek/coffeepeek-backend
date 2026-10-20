using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.Login;
using CoffeePeek.Account.Domain;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.Login;

public class LoginUserHandlerTests
{
    private readonly Mock<IJWTTokenService> _jwtMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IPasswordHasherService> _hasherMock = new();
    private readonly IOptions<JWTOptions> _jwtOptions = Options.Create(new JWTOptions
    {
        SecretKey = "test-secret-key-must-be-32-chars!",
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 7
    });
    private readonly CancellationToken _ct = CancellationToken.None;

    private Task<CoffeePeek.Shared.Kernel.Response.Response<LoginResponse>> Login(string email, string password,
        string device, string ip, CancellationToken ct = default) =>
        LoginUserHandler.Handle(new LoginUserCommand(email, password, device, ip), _userRepoMock.Object,
            _hasherMock.Object, _jwtMock.Object, _jwtOptions, ct);

    [Fact]
    public void Command_ToString_DoesNotExposeSubmittedPassword()
    {
        const string password = "secret, with } delimiters and spaces";
        var command = new LoginUserCommand("user@example.com", password, "Chrome", "127.0.0.1");

        command.ToString().Should().Contain("[REDACTED]");
        command.ToString().Should().NotContain(password);
    }

    private static DomainUser CreateConfirmedUser(string email = "user@example.com", string hash = "hashed")
    {
        var role = Role.Create("User");
        var user = DomainUser.Register(email, "testuser", hash, role);
        typeof(UserCredential)
            .GetProperty(nameof(UserCredential.EmailConfirmed))!
            .SetValue(user.Credentials, true);
        return user;
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsAuthResult()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "password123";
        const string hash = "hashed";
        var user = CreateConfirmedUser(email, hash);

        _userRepoMock.Setup(r => r.GetByEmail(email, _ct)).ReturnsAsync(user);
        _hasherMock.Setup(h => h.VerifyPassword(hash, password)).Returns(true);
        _jwtMock.Setup(j => j.GenerateAccessToken(user)).Returns("access_token");
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("refresh_token");

        // Act
        var result = await Login(email, password, "device", "ip", _ct);

        // Assert
        result.Data!.AccessToken.Should().Be("access_token");
        result.Data!.RefreshToken.Should().Be("refresh_token");
        user.RefreshTokens.Should().ContainSingle(t => t.DeviceName == "device" && t.IpAddress == "ip");
        result.Data!.AccessTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_WithNonExistentEmail_ThrowsUnauthorizedException()
    {
        // Arrange
        _userRepoMock.Setup(r => r.GetByEmail(It.IsAny<string>(), _ct)).ReturnsAsync((DomainUser?)null);

        // Act
        Func<Task> act = () => Login("nobody@example.com", "pass", "device", "ip");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task Handle_WithWrongPassword_ThrowsUnauthorizedException()
    {
        // Arrange
        var user = CreateConfirmedUser();
        _userRepoMock.Setup(r => r.GetByEmail(It.IsAny<string>(), _ct)).ReturnsAsync(user);
        _hasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        // Act
        Func<Task> act = () => Login("user@example.com", "wrong", "device", "ip");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task Handle_WithPasswordlessExternalAccount_ReturnsSameErrorAsWrongPassword()
    {
        var user = DomainUser.CreateExternal("user@example.com", "google", "provider-id");
        _userRepoMock.Setup(r => r.GetByEmail("user@example.com", _ct)).ReturnsAsync(user);

        Func<Task> act = () => Login("user@example.com", "submitted-password", "device", "ip", _ct);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid credentials");
        _hasherMock.VerifyNoOtherCalls();
        _jwtMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WithUnconfirmedEmail_ThrowsUnauthorizedException()
    {
        // Arrange — Register creates user with EmailConfirmed=false
        var role = Role.Create("User");
        var user = DomainUser.Register("user@example.com", "testuser", "hashed", role);
        _userRepoMock.Setup(r => r.GetByEmail(It.IsAny<string>(), _ct)).ReturnsAsync(user);
        _hasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        // Act
        Func<Task> act = () => Login("user@example.com", "password", "device", "ip");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Email is not confirmed");
    }

    [Fact]
    public async Task Handle_WithValidCredentials_KeepsExistingSessionsWhenUnderLimit()
    {
        // Arrange
        var user = CreateConfirmedUser();
        user.AddSession("old_token", TimeSpan.FromDays(7), "device", "ip");

        _userRepoMock.Setup(r => r.GetByEmail(It.IsAny<string>(), _ct)).ReturnsAsync(user);
        _hasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _jwtMock.Setup(j => j.GenerateAccessToken(user)).Returns("access");
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("new_refresh");

        // Act
        await Login("user@example.com", "password", "device", "ip", _ct);

        // Assert — under MaxActiveSessions, existing sessions stay active
        user.RefreshTokens.Should().Contain(t => t.Token == "old_token" && t.IsActive);
        user.RefreshTokens.Should().Contain(t => t.Token == "new_refresh" && t.IsActive);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_RevokesOldestSessionWhenAtLimit()
    {
        // Arrange
        var user = CreateConfirmedUser();
        for (var i = 0; i < BusinessConstants.MaxActiveSessions; i++)
            user.AddSession($"session_{i}", TimeSpan.FromDays(7), "device", "ip");

        _userRepoMock.Setup(r => r.GetByEmail(It.IsAny<string>(), _ct)).ReturnsAsync(user);
        _hasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _jwtMock.Setup(j => j.GenerateAccessToken(user)).Returns("access");
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("new_refresh");

        // Act
        await Login("user@example.com", "password", "device", "ip", _ct);

        // Assert — oldest session evicted, new one added; others remain active
        user.RefreshTokens.First(t => t.Token == "session_0").IsActive.Should().BeFalse();
        user.RefreshTokens.Count(t => t.IsActive).Should().Be(BusinessConstants.MaxActiveSessions);
        user.RefreshTokens.Should().Contain(t => t.Token == "new_refresh" && t.IsActive);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_CreatesNewSession()
    {
        // Arrange
        var user = CreateConfirmedUser();
        _userRepoMock.Setup(r => r.GetByEmail(It.IsAny<string>(), _ct)).ReturnsAsync(user);
        _hasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _jwtMock.Setup(j => j.GenerateAccessToken(user)).Returns("access");
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("fresh_token");

        // Act
        await Login("user@example.com", "password", "device", "ip");

        // Assert
        user.RefreshTokens.Should().ContainSingle(t => t.Token == "fresh_token" && t.IsActive);
    }
}
