using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public class UserTests
{
    [Fact]
    public void Register_WithValidParameters_ShouldCreateUser()
    {
        // Arrange
        const string email = "test@example.com";
        const string username = "testuser";
        const string passwordHash = "hashed_password";

        // Act
        var user = User.Register(email, username, passwordHash, Role.Create("User"));

        // Assert
        user.Should().NotBeNull();
        user.Id.Should().NotBe(Guid.Empty);
        user.Username.Value.Should().Be(username);
        user.Credentials.Email.Value.Should().Be(email);
        user.Credentials.PasswordHash.Should().Be(passwordHash);
        user.Statistics.Should().NotBeNull();
        user.IsSoftDelete.Should().BeFalse();
    }

    [Fact]
    public void Register_ShouldRaiseDomainEvent()
    {
        // Arrange
        const string email = "test@example.com";
        const string username = "testuser";
        const string passwordHash = "hashed_password";

        // Act
        var user = User.Register(email, username, passwordHash, Role.Create("User"));

        // Assert
        var domainEvents = user.GetDomainEvents();
        domainEvents.Should().ContainSingle();
        domainEvents.First().Should().BeOfType<UserRegisteredInternalEvent>();
        
        var registeredEvent = (UserRegisteredInternalEvent)domainEvents.First();
        registeredEvent.UserId.Should().Be(user.Id);
        registeredEvent.Email.Should().Be(email);
    }

    [Theory]
    [InlineData("invalid-email", "username")]
    [InlineData("test@example.com", "ab")] // Username too short
    [InlineData("test@example.com", "")] // Username empty
    public void Register_WithInvalidParameters_ShouldThrowDomainException(string email, string username)
    {
        // Act
        Action act = () => User.Register(email, username, "hash", Role.Create("User"));

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CreateExternal_WithValidParameters_ShouldCreateUser()
    {
        // Arrange
        const string email = "test@example.com";
        const string provider = "google";
        const string providerId = "google_123";

        // Act
        var user = User.CreateExternal(email, provider, providerId);

        // Assert
        user.Should().NotBeNull();
        user.Id.Should().NotBe(Guid.Empty);
        user.Username.Value.Should().Be("test"); // Email prefix
        user.Credentials.Email.Value.Should().Be(email);
        user.Credentials.OAuthProvider.Should().Be(provider);
        user.Credentials.ProviderId.Should().Be(providerId);
        user.Credentials.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public void CreateExternal_WithUsernameOverride_ShouldUseOverride()
    {
        var user = User.CreateExternal("test@example.com", "google", "google_123", "customuser");

        user.Username.Value.Should().Be("customuser");
        user.Credentials.Email.Value.Should().Be("test@example.com");
    }

    [Fact]
    public void CreateExternal_WithCyrillicEmailLocalPart_ShouldPreserveCyrillicUsername()
    {
        var user = User.CreateExternal("иван@example.com", "google", "provider-id");

        user.Username.Value.Should().Be("иван");
    }

    [Fact]
    public void ChangePassword_ShouldRevokeAllRefreshSessions()
    {
        var user = User.Register("test@example.com", "testuser", "old_hash", Role.Create("User"));
        user.AddSession("refresh1", TimeSpan.FromHours(1), "device", "127.0.0.1");
        user.AddSession("refresh2", TimeSpan.FromHours(1), "device", "127.0.0.1");

        user.ChangePassword("new_hash");

        user.Credentials.PasswordHash.Should().Be("new_hash");
        user.RefreshTokens.Should().OnlyContain(t => !t.IsActive);
        user.RefreshTokens.Should().HaveCount(2);
    }

    [Fact]
    public void AddSession_ShouldAddRefreshToken()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        const string token = "refresh_token_123";
        var ttl = TimeSpan.FromDays(7);
        const string device = "Chrome/Windows";
        const string ip = "192.168.1.1";

        // Act
        user.AddSession(token, ttl, device, ip);

        // Assert
        user.RefreshTokens.Should().ContainSingle();
        var refreshToken = user.RefreshTokens.First();
        refreshToken.Token.Should().Be(token);
        refreshToken.IsActive.Should().BeTrue();
    }

    [Fact]
    public void AddSession_WhenMaxSessionsReached_ShouldRevokeOldest()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        var ttl = TimeSpan.FromDays(7);
        
        // Add max number of sessions (BusinessConstants.MaxActiveSessions)
        // Assuming MaxActiveSessions = 5
        for (int i = 0; i < 5; i++)
        {
            user.AddSession($"token_{i}", ttl, "device", "ip");
            Thread.Sleep(10); // Ensure different timestamps
        }

        // Act - Add one more session
        user.AddSession("new_token", ttl, "device", "ip");

        // Assert
        user.RefreshTokens.Should().HaveCount(6); // 5 + 1 new
        user.RefreshTokens.Count(t => t.IsActive).Should().Be(5);
        user.RefreshTokens.First().IsActive.Should().BeFalse(); // Oldest revoked
    }

    [Fact]
    public void RotateRefreshToken_WithValidToken_ShouldRevokeOldAndAddNew()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        const string oldToken = "old_token";
        const string newToken = "new_token";
        var ttl = TimeSpan.FromDays(7);
        
        user.AddSession(oldToken, ttl, "device", "ip");

        // Act
        user.RotateRefreshToken(oldToken, newToken, ttl, "device", "ip");

        // Assert
        user.RefreshTokens.Should().HaveCount(2);
        user.RefreshTokens.Count(t => t.IsActive).Should().Be(1);
        user.RefreshTokens.Should().Contain(t => t.Token == newToken && t.IsActive);
        user.RefreshTokens.Should().Contain(t => t.Token == oldToken && !t.IsActive);
    }

    [Fact]
    public void RotateRefreshToken_WithInvalidToken_ShouldRevokeAllAndThrow()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        user.AddSession("valid_token", TimeSpan.FromDays(7), "device", "ip");

        // Act
        Action act = () => user.RotateRefreshToken("invalid_token", "new_token", TimeSpan.FromDays(7), "device", "ip");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*Security breach*");
        user.RefreshTokens.Should().OnlyContain(t => !t.IsActive);
    }

    [Fact]
    public void RevokeAllSessions_ShouldDeactivateAllTokens()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        var ttl = TimeSpan.FromDays(7);
        
        user.AddSession("token1", ttl, "device", "ip");
        user.AddSession("token2", ttl, "device", "ip");
        user.AddSession("token3", ttl, "device", "ip");

        // Act
        user.RevokeAllSessions();

        // Assert
        user.RefreshTokens.Should().OnlyContain(t => !t.IsActive);
    }

    [Fact]
    public void AssignRole_ShouldAddRoleToUser()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        var role = Role.Create("Admin");

        // Act
        user.AssignRole(role);

        // Assert
        user.Roles.Should().Contain(role);
    }

    [Fact]
    public void UpdateProfile_WithValidData_ShouldUpdateProfile()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        const string newAbout = "New bio";

        // Act
        user.UpdateAbout(newAbout);

        // Assert
        user.About.Should().Be(newAbout);
    }

    [Fact]
    public void UpdateAvatar_ShouldSetPhotoMetadata()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        var photo = PhotoMetadata.Create("avatar.jpg", "image/jpeg", "key", 1024);

        // Act
        user.UpdateAvatar(photo);

        // Assert
        user.PhotoMetadataId.Should().Be(photo.Id);
        user.PhotoMetadata.Should().Be(photo);
    }

    [Fact]
    public void SoftDelete_ShouldMarkUserAsDeleted()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));

        // Act
        user.SetSoftDelete();

        // Assert
        user.IsSoftDelete.Should().BeTrue();
    }

    [Fact]
    public void ConfirmEmail_ShouldUpdateCredentials()
    {
        // Arrange
        var user = User.Register("test@example.com", "testuser", "hash",Role.Create("User"));
        var token = user.Credentials.EmailConfirmationToken;

        // Act
        user.ConfirmEmail(token!);

        // Assert
        user.Credentials.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public void UpdateEmail_ShouldChangeEmailAndResetConfirmation()
    {
        // Arrange
        var user = User.Register("old@example.com", "testuser", "hash",Role.Create("User"));
        user.ConfirmEmail(user.Credentials.EmailConfirmationToken!);
        const string newEmail = "new@example.com";

        // Act
        user.Credentials.UpdateEmail(newEmail);

        // Assert
        user.Credentials.Email.Value.Should().Be(newEmail);
        user.Credentials.EmailConfirmed.Should().BeFalse();
    }
}
