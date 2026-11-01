using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public class RefreshTokenAdditionalTests
{
    [Fact]
    public void Create_WithNullToken_ShouldThrowArgumentException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string token = null;
        var ttl = TimeSpan.FromDays(7);
        const string device = "Chrome/Windows";
        const string ip = "192.168.1.1";

        // Act & Assert
        var act = () => RefreshToken.Create(userId, token, ttl, device, ip);
        act.Should().Throw<ArgumentException>().WithMessage("Token is required");
    }

    [Fact]
    public void Create_WithEmptyToken_ShouldThrowArgumentException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ttl = TimeSpan.FromDays(7);
        const string device = "Chrome/Windows";
        const string ip = "192.168.1.1";

        // Act & Assert
        var act = () => RefreshToken.Create(userId, "", ttl, device, ip);
        act.Should().Throw<ArgumentException>().WithMessage("Token is required");
    }

    [Fact]
    public void Create_WithWhitespaceToken_ShouldThrowArgumentException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ttl = TimeSpan.FromDays(7);
        const string device = "Chrome/Windows";
        const string ip = "192.168.1.1";

        // Act & Assert
        var act = () => RefreshToken.Create(userId, "   ", ttl, device, ip);
        act.Should().Throw<ArgumentException>().WithMessage("Token is required");
    }

    [Fact]
    public void IsActive_WhenExpiredAndNotRevoked_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var expiredRefreshToken = RefreshToken.Create(userId, "expired_token", TimeSpan.FromMilliseconds(-1), "device", "ip");
        
        // Allow a small delay to ensure time has passed
        Thread.Sleep(10);

        // Act
        var isActive = expiredRefreshToken.IsActive;

        // Assert
        isActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_WhenNotExpiredAndNotRevoked_ShouldReturnTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var validRefreshToken = RefreshToken.Create(userId, "valid_token", TimeSpan.FromDays(1), "device", "ip");

        // Act
        var isActive = validRefreshToken.IsActive;

        // Assert
        isActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_WhenExpiredAndRevoked_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var expiredRefreshToken = RefreshToken.Create(userId, "expired_and_revoked_token", TimeSpan.FromMilliseconds(-1), "device", "ip");
        
        // Allow a small delay to ensure time has passed
        Thread.Sleep(10);
        
        // Revoke the token
        expiredRefreshToken.Revoke();

        // Act
        var isActive = expiredRefreshToken.IsActive;

        // Assert
        isActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_WhenNotExpiredButRevoked_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var validRefreshToken = RefreshToken.Create(userId, "valid_but_revoked_token", TimeSpan.FromDays(1), "device", "ip");
        
        // Revoke the token
        validRefreshToken.Revoke();

        // Act
        var isActive = validRefreshToken.IsActive;

        // Assert
        isActive.Should().BeFalse();
    }

    [Fact]
    public void Create_WithZeroTtl_ShouldCreateTokenThatExpiresImmediately()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ttl = TimeSpan.Zero;
        var beforeCreation = DateTime.UtcNow;

        // Act
        var refreshToken = RefreshToken.Create(userId, "zero_ttl_token", ttl, "device", "ip");
        Thread.Sleep(10); // Ensure time has passed

        // Assert
        refreshToken.ExpiryDate.Should().BeCloseTo(beforeCreation, TimeSpan.FromMilliseconds(100));
        refreshToken.IsActive.Should().BeFalse(); // Should be expired immediately
    }

    [Fact]
    public void Create_WithVeryLargeTtl_ShouldCapExpirationAtSessionDeadline()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ttl = TimeSpan.FromDays(365 * 10); // 10 years

        // Act
        var refreshToken = RefreshToken.Create(userId, "long_lived_token", ttl, "device", "ip");

        // Assert
        refreshToken.IsActive.Should().BeTrue();
        refreshToken.ExpiryDate.Should().Be(refreshToken.SessionExpiresAtUtc);
        refreshToken.SessionExpiresAtUtc.Should().Be(refreshToken.CreatedAtUtc.AddDays(365));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithNullOrWhitespaceDevice_ShouldAllowCreation(string device)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ttl = TimeSpan.FromDays(7);

        // Act
        var refreshToken = RefreshToken.Create(userId, "token", ttl, device, "ip");

        // Assert
        refreshToken.DeviceName.Should().Be(device);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithNullOrWhitespaceIp_ShouldAllowCreation(string ip)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ttl = TimeSpan.FromDays(7);

        // Act
        var refreshToken = RefreshToken.Create(userId, "token", ttl, "device", ip);

        // Assert
        refreshToken.IpAddress.Should().Be(ip);
    }

    [Fact]
    public void Revoke_UpdatesUpdatedAt()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var refreshToken = RefreshToken.Create(userId, "token", TimeSpan.FromDays(7), "device", "ip");
        var beforeRevoke = DateTime.UtcNow;

        // Act
        refreshToken.Revoke();
        var afterRevoke = DateTime.UtcNow;

        // Assert
        refreshToken.IsRevoked.Should().BeTrue();
        refreshToken.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Revoke_DoesNotUpdateUpdatedAtWhenAlreadyRevoked()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var refreshToken = RefreshToken.Create(userId, "token", TimeSpan.FromDays(7), "device", "ip");
        refreshToken.Revoke();
        var firstRevokeTime = refreshToken.UpdatedAtUtc;
        
        Thread.Sleep(10); // Small delay to ensure potential time difference

        // Act
        refreshToken.Revoke(); // Second revoke call

        // Assert
        refreshToken.UpdatedAtUtc.Should().Be(firstRevokeTime); // Should remain unchanged
    }

    [Fact]
    public void UserId_IsCorrectlySet()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var refreshToken = RefreshToken.Create(userId, "token", TimeSpan.FromDays(7), "device", "ip");

        // Assert
        refreshToken.UserId.Should().Be(userId);
    }

    [Fact]
    public void Token_PropertyIsCorrectlySet()
    {
        // Arrange
        const string token = "my_refresh_token";

        // Act
        var refreshToken = RefreshToken.Create(Guid.NewGuid(), token, TimeSpan.FromDays(7), "device", "ip");

        // Assert
        refreshToken.Token.Should().Be(token);
    }

    [Fact]
    public void ExpiryDate_CalculatedCorrectlyFromTtl()
    {
        // Arrange
        var ttl = TimeSpan.FromHours(2);
        var beforeCreation = DateTime.UtcNow;

        // Act
        var refreshToken = RefreshToken.Create(Guid.NewGuid(), "token", ttl, "device", "ip");
        var expectedExpiry = beforeCreation.Add(ttl);

        // Assert
        refreshToken.ExpiryDate.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(1));
    }
}