using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.OAuthLogin;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.OAuth;

public class GoogleLoginHandlerTests
{
    private readonly Mock<IGoogleAuthService> _googleAuthMock = new();
    private readonly Mock<IExternalAuthService> _externalAuthMock = new();
    private readonly Mock<IJWTTokenService> _tokenServiceMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly IOptions<JWTOptions> _jwtOptions = Options.Create(new JWTOptions
    {
        SecretKey = "test-secret-key-must-be-32-chars!",
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 7
    });
    private readonly CancellationToken _ct = CancellationToken.None;

    private Task<Response<GoogleLoginResponse>> Handle(GoogleLoginCommand command) =>
        GoogleLoginHandler.Handle(
            command,
            _googleAuthMock.Object,
            _externalAuthMock.Object,
            _tokenServiceMock.Object,
            _jwtOptions,
            _unitOfWorkMock.Object,
            _ct);

    [Fact]
    public async Task Handle_WhenPayloadIsNull_ReturnsInvalidTokenWithoutGetOrCreate()
    {
        _googleAuthMock.Setup(s => s.ValidateIdTokenAsync("bad-token", _ct))
            .ReturnsAsync((GoogleJsonWebSignature.Payload?)null);

        var response = await Handle(new GoogleLoginCommand("bad-token"));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Invalid token");
        _externalAuthMock.Verify(
            s => s.GetOrCreate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEmailNotVerified_ReturnsBadRequestWithoutGetOrCreate()
    {
        _googleAuthMock.Setup(s => s.ValidateIdTokenAsync("id-token", _ct))
            .ReturnsAsync(new GoogleJsonWebSignature.Payload
            {
                Email = "user@gmail.com",
                EmailVerified = false,
                Subject = "google-sub"
            });

        var response = await Handle(new GoogleLoginCommand("id-token"));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        response.Message.Should().Be("Google email is not verified.");
        _externalAuthMock.Verify(
            s => s.GetOrCreate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEmailVerified_CreatesSession()
    {
        var user = DomainUser.CreateExternal("user@gmail.com", "google", "google-sub");
        user.AssignRole(Role.Create("User"));

        _googleAuthMock.Setup(s => s.ValidateIdTokenAsync("id-token", _ct))
            .ReturnsAsync(new GoogleJsonWebSignature.Payload
            {
                Email = "user@gmail.com",
                EmailVerified = true,
                Subject = "google-sub",
                Picture = "https://example.com/avatar.png"
            });
        _externalAuthMock.Setup(s => s.GetOrCreate("user@gmail.com", "GoogleProvider", "google-sub", _ct))
            .ReturnsAsync(user);
        _tokenServiceMock.Setup(s => s.GenerateAccessToken(user)).Returns("access");
        _tokenServiceMock.Setup(s => s.GenerateRefreshToken()).Returns("refresh");

        var response = await Handle(new GoogleLoginCommand("id-token", "Chrome", "127.0.0.1"));

        response.IsSuccess.Should().BeTrue();
        response.Data!.AccessToken.Should().Be("access");
        response.Data.RefreshToken.Should().Be("refresh");
        response.Data.User.Email.Should().Be("user@gmail.com");
        user.RefreshTokens.Should().ContainSingle(t => t.IsActive && t.Token == "refresh");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public void GoogleLoginResponse_SerializesWithoutRefreshToken()
    {
        var payload = new GoogleLoginResponse(
            "access",
            "secret-refresh",
            DateTime.UtcNow.AddMinutes(15),
            new GoogleLoginUser
            {
                Email = "user@gmail.com",
                Username = "user",
                AvatarUrl = "https://example.com/a.png"
            });
        var response = Response<GoogleLoginResponse>.Success(payload);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("access");
        json.Should().Contain("user@gmail.com");
        json.Should().NotContain("secret-refresh");
    }
}
