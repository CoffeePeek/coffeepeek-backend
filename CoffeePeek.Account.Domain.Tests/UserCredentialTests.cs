using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public class UserCredentialTests
{
    [Fact]
    public void CreateBasic_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        const string hashedPassword = "hashed_password_123";
        const string token = "confirmation_token_123";

        // Act
        var credential = UserCredential.CreateBasic(email, hashedPassword, token);

        // Assert
        credential.Should().NotBeNull();
        credential.Email.Should().Be(email);
        credential.PasswordHash.Should().Be(hashedPassword);
        credential.EmailConfirmed.Should().BeFalse();
        credential.EmailConfirmationToken.Should().Be(token);
        credential.EmailConfirmationExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(5));
        credential.OAuthProvider.Should().BeNull();
        credential.ProviderId.Should().BeNull();
    }

    [Fact]
    public void CreateExternal_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        const string provider = "google";
        const string providerId = "google_123";

        // Act
        var credential = UserCredential.CreateExternal(email, provider, providerId);

        // Assert
        credential.Should().NotBeNull();
        credential.Email.Should().Be(email);
        credential.OAuthProvider.Should().Be(provider);
        credential.ProviderId.Should().Be(providerId);
        credential.EmailConfirmed.Should().BeTrue();
        credential.PasswordHash.Should().BeEmpty();
        credential.EmailConfirmationToken.Should().BeNull();
    }

    [Fact]
    public void ConfirmEmail_WithValidToken_ShouldSucceed()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        const string token = "valid_token";
        var credential = UserCredential.CreateBasic(email, "hash", token);

        // Act
        var confirmed = credential.ConfirmEmail(token);

        // Assert
        confirmed.EmailConfirmed.Should().BeTrue();
        confirmed.EmailConfirmationToken.Should().BeNull();
        confirmed.EmailConfirmationExpiresAt.Should().BeNull();
    }

    [Fact]
    public void ConfirmEmail_WithInvalidToken_ShouldThrowDomainException()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        var credential = UserCredential.CreateBasic(email, "hash", "correct_token");

        // Act
        Action act = () => credential.ConfirmEmail("wrong_token");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*Invalid token*");
    }

    [Fact]
    public void ConfirmEmail_WithExpiredToken_ShouldThrowDomainException()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        const string token = "expired_token";
        var credential = UserCredential.CreateBasic(email, "hash", token);

        // Set expiry to the past via reflection
        typeof(UserCredential)
            .GetProperty(nameof(UserCredential.EmailConfirmationExpiresAt))!
            .SetValue(credential, DateTime.UtcNow.AddMinutes(-1));

        // Act
        Action act = () => credential.ConfirmEmail(token);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*expired*");
    }

    [Fact]
    public void ValidatePassword_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        const string password = "correct_password";
        const string hashedPassword = "hashed_password";
        var credential = UserCredential.CreateBasic(email, hashedPassword, "token");
        
        var mockHasher = new Mock<IPasswordHasherService>();
        mockHasher.Setup(h => h.VerifyPassword(hashedPassword, password)).Returns(true);

        // Act
        var result = credential.ValidatePassword(password, mockHasher.Object);

        // Assert
        result.Should().BeTrue();
        mockHasher.Verify(h => h.VerifyPassword(hashedPassword, password), Times.Once);
    }

    [Fact]
    public void ValidatePassword_ForExternalCredential_ReturnsFalseWithoutCallingHasher()
    {
        var credential = UserCredential.CreateExternal(Email.Create("user@example.com"), "google", "provider-id");
        var hasher = new Mock<IPasswordHasherService>(MockBehavior.Strict);

        credential.ValidatePassword("submitted-password", hasher.Object).Should().BeFalse();

        hasher.VerifyNoOtherCalls();
    }

    [Fact]
    public void LinkExternalProvider_WithNoExistingProvider_ShouldSucceed()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        var credential = UserCredential.CreateBasic(email, "hash", "token");

        // Act
        credential.LinkExternalProvider("google", "google_123");

        // Assert
        credential.OAuthProvider.Should().Be("google");
        credential.ProviderId.Should().Be("google_123");
    }

    [Fact]
    public void LinkExternalProvider_WithSameProvider_ShouldSucceed()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        var credential = UserCredential.CreateExternal(email, "google", "google_123");

        // Act
        credential.LinkExternalProvider("google", "google_456");

        // Assert
        credential.OAuthProvider.Should().Be("google");
        credential.ProviderId.Should().Be("google_456");
    }

    [Fact]
    public void LinkExternalProvider_WithDifferentProvider_ShouldThrowDomainException()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        var credential = UserCredential.CreateExternal(email, "google", "google_123");

        // Act
        Action act = () => credential.LinkExternalProvider("facebook", "fb_123");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*already linked to another provider*");
    }

    [Fact]
    public void UpdateEmail_WithValidEmail_ShouldUpdateAndResetConfirmation()
    {
        // Arrange
        var oldEmail = Email.Create("old@example.com");
        var credential = UserCredential.CreateBasic(oldEmail, "hash", "token");
        var confirmedCredential = credential.ConfirmEmail("token");

        // Act
        confirmedCredential.UpdateEmail("new@example.com");

        // Assert
        confirmedCredential.Email.Value.Should().Be("new@example.com");
        confirmedCredential.EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public void UpdateEmail_WithInvalidEmail_ShouldThrowDomainException()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        var credential = UserCredential.CreateBasic(email, "hash", "token");

        // Act
        Action act = () => credential.UpdateEmail("invalid-email");

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("google")]
    [InlineData("facebook")]
    [InlineData("github")]
    [InlineData("twitter")]
    public void CreateExternal_WithDifferentProviders_ShouldMaintainProviderName(string provider)
    {
        // Arrange
        var email = Email.Create("test@example.com");

        // Act
        var credential = UserCredential.CreateExternal(email, provider, "provider_id");

        // Assert
        credential.OAuthProvider.Should().Be(provider);
    }

    [Fact]
    public void CreateBasic_ShouldSetExpirationTo10MinutesFromNow()
    {
        // Arrange
        var email = Email.Create("test@example.com");
        var beforeCreation = DateTime.UtcNow;

        // Act
        var credential = UserCredential.CreateBasic(email, "hash", "token");
        var afterCreation = DateTime.UtcNow;

        // Assert
        credential.EmailConfirmationExpiresAt.Should().NotBeNull();
        credential.EmailConfirmationExpiresAt.Value.Should().BeAfter(beforeCreation.AddMinutes(9));
        credential.EmailConfirmationExpiresAt.Value.Should().BeBefore(afterCreation.AddMinutes(11));
    }
}
