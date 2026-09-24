using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Infrastructure.Identity;
using CoffeePeek.Shared.Auth.Options;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace CoffeePeek.Account.Infrastructure.Tests.Identity;

public class JWTTokenServiceTests
{
    private const string SecretKey = "test-secret-key-must-be-32-chars!!";
    private const string Issuer = "TestIssuer";
    private const string Audience = "TestAudience";
    private const int LifetimeMinutes = 15;

    private static JWTTokenService CreateSut(int lifetimeMinutes = LifetimeMinutes)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new JWTOptions
        {
            SecretKey = SecretKey,
            Issuer = Issuer,
            Audience = Audience,
            AccessTokenLifetimeMinutes = lifetimeMinutes,
            RefreshTokenLifetimeDays = 7
        });
        return new JWTTokenService(options);
    }

    private static User CreateConfirmedUser(string email = "user@example.com")
    {
        var role = Role.Create("User");
        var user = User.Register(email, "testuser", "hash", role);
        typeof(UserCredential)
            .GetProperty(nameof(UserCredential.EmailConfirmed))!
            .SetValue(user.Credentials, true);
        return user;
    }

    private JwtSecurityToken ParseToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));

        handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out var validated);

        return (JwtSecurityToken)validated;
    }

    [Fact]
    public void GenerateAccessToken_ContainsCorrectSubClaim()
    {
        // Arrange
        var user = CreateConfirmedUser();
        var sut = CreateSut();

        // Act
        var token = sut.GenerateAccessToken(user);
        var parsed = ParseToken(token);

        // Assert
        parsed.Subject.Should().Be(user.Id.ToString());
    }

    [Fact]
    public void GenerateAccessToken_ContainsIssuedAtClaim()
    {
        // Gateway access-token revocation depends on iat; without it every revoked user stays locked out.
        var sut = CreateSut();

        var parsed = ParseToken(sut.GenerateAccessToken(CreateConfirmedUser()));

        parsed.IssuedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GenerateAccessToken_ContainsCorrectEmailClaim()
    {
        // Arrange
        var user = CreateConfirmedUser("user@example.com");
        var sut = CreateSut();

        // Act
        var token = sut.GenerateAccessToken(user);
        var parsed = ParseToken(token);

        // Assert
        parsed.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "user@example.com");
    }

    [Fact]
    public void GenerateAccessToken_WhenEmailConfirmed_EmailVerifiedIsTrue()
    {
        // Arrange
        var user = CreateConfirmedUser();
        user.Credentials.EmailConfirmed.Should().BeTrue();
        var sut = CreateSut();

        // Act
        var token = sut.GenerateAccessToken(user);
        var parsed = ParseToken(token);

        // Assert
        parsed.Claims.Should().Contain(c =>
            c.Type == JwtRegisteredClaimNames.EmailVerified && c.Value == "true");
    }

    [Fact]
    public void GenerateAccessToken_WhenEmailNotConfirmed_EmailVerifiedIsFalse()
    {
        // Arrange
        var role = Role.Create("User");
        var user = User.Register("user@example.com", "testuser", "hash", role);
        user.Credentials.EmailConfirmed.Should().BeFalse();
        var sut = CreateSut();

        // Act
        var token = sut.GenerateAccessToken(user);
        var parsed = ParseToken(token);

        // Assert
        parsed.Claims.Should().Contain(c =>
            c.Type == JwtRegisteredClaimNames.EmailVerified && c.Value == "false");
    }

    [Fact]
    public void GenerateAccessToken_ContainsRoleClaims()
    {
        // Arrange
        var user = CreateConfirmedUser();
        var sut = CreateSut();

        // Act
        var token = sut.GenerateAccessToken(user);
        var parsed = ParseToken(token);

        // Assert
        parsed.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public void GenerateAccessToken_ExpiresInCorrectMinutes_NotHours()
    {
        // Arrange — C-1 regression: must be AddMinutes, not AddHours
        var user = CreateConfirmedUser();
        var before = DateTime.UtcNow;
        var sut = CreateSut(lifetimeMinutes: 15);

        // Act
        var token = sut.GenerateAccessToken(user);
        var parsed = ParseToken(token);

        // Assert
        var expected = before.AddMinutes(15);
        parsed.ValidTo.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));

        // Critical: token must NOT expire in 15 hours
        parsed.ValidTo.Should().BeBefore(before.AddHours(1));
    }

    [Fact]
    public void GenerateAccessToken_HasCorrectIssuerAndAudience()
    {
        // Arrange
        var user = CreateConfirmedUser();
        var sut = CreateSut();

        // Act
        var token = sut.GenerateAccessToken(user);
        var parsed = ParseToken(token);

        // Assert
        parsed.Issuer.Should().Be(Issuer);
        parsed.Audiences.Should().Contain(Audience);
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsNonEmptyBase64String()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var token = sut.GenerateRefreshToken();

        // Assert
        token.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(token);
        bytes.Should().HaveCount(32, "refresh token must be 32 random bytes");
    }

    [Fact]
    public void GenerateRefreshToken_EachCallReturnsUniqueToken()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var tokens = Enumerable.Range(0, 10).Select(_ => sut.GenerateRefreshToken()).ToList();

        // Assert
        tokens.Distinct().Should().HaveCount(10, "each refresh token must be unique");
    }
}
