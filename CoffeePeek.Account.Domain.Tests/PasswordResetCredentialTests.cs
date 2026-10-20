using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public class PasswordResetCredentialTests
{
    [Fact]
    public void BeginPasswordReset_WithPasswordAuth_SetsTokenAndExpiry()
    {
        var credential = UserCredential.CreateBasic(Email.Create("test@example.com"), "hash", "confirm");
        var before = DateTime.UtcNow;

        credential.BeginPasswordReset();
        var after = DateTime.UtcNow;

        credential.PasswordResetToken.Should().NotBeNullOrEmpty();
        credential.PasswordResetToken!.Length.Should().Be(32);
        credential.PasswordResetExpiresAt.Should().NotBeNull();
        credential.PasswordResetExpiresAt!.Value.Should().BeAfter(before.AddMinutes(29));
        credential.PasswordResetExpiresAt.Value.Should().BeBefore(after.AddMinutes(31));
    }

    [Fact]
    public void BeginPasswordReset_ForOAuthOnly_ThrowsDomainException()
    {
        var credential = UserCredential.CreateExternal(Email.Create("oauth@example.com"), "google", "gid");

        Action act = () => credential.BeginPasswordReset();

        act.Should().Throw<DomainException>().WithMessage("*Password login not available*");
    }

    [Fact]
    public void ChangePassword_UpdatesHashAndClearsReset()
    {
        var credential = UserCredential.CreateBasic(Email.Create("test@example.com"), "old_hash", "confirm");
        credential.BeginPasswordReset();

        var updated = credential.ChangePassword("new_hash");

        updated.PasswordHash.Should().Be("new_hash");
        updated.PasswordResetToken.Should().BeNull();
        updated.PasswordResetExpiresAt.Should().BeNull();
    }

    [Fact]
    public void ChangePassword_ForOAuthOnly_ThrowsDomainException()
    {
        var credential = UserCredential.CreateExternal(Email.Create("oauth@example.com"), "google", "gid");

        Action act = () => credential.ChangePassword("new_hash");

        act.Should().Throw<DomainException>().WithMessage("*Password login not available*");
    }

    [Fact]
    public void CompletePasswordReset_WithValidToken_UpdatesHashAndClearsToken()
    {
        var credential = UserCredential.CreateBasic(Email.Create("test@example.com"), "old_hash", "confirm");
        credential.BeginPasswordReset();
        var token = credential.PasswordResetToken!;

        var updated = credential.CompletePasswordReset(token, "new_hash");

        updated.PasswordHash.Should().Be("new_hash");
        updated.PasswordResetToken.Should().BeNull();
        updated.PasswordResetExpiresAt.Should().BeNull();
    }

    [Fact]
    public void CompletePasswordReset_WithInvalidToken_ThrowsDomainException()
    {
        var credential = UserCredential.CreateBasic(Email.Create("test@example.com"), "hash", "confirm");
        credential.BeginPasswordReset();

        Action act = () => credential.CompletePasswordReset("wrong_token", "new_hash");

        act.Should().Throw<DomainException>().WithMessage("*Invalid token*");
    }

    [Fact]
    public void CompletePasswordReset_WithExpiredToken_ThrowsDomainException()
    {
        var credential = UserCredential.CreateBasic(Email.Create("test@example.com"), "hash", "confirm");
        credential.BeginPasswordReset();
        var token = credential.PasswordResetToken!;
        typeof(UserCredential).GetProperty(nameof(UserCredential.PasswordResetExpiresAt))!
            .SetValue(credential, DateTime.UtcNow.AddMinutes(-1));

        Action act = () => credential.CompletePasswordReset(token, "new_hash");

        act.Should().Throw<DomainException>().WithMessage("*expired*");
    }

    [Fact]
    public void User_ResetPassword_RevokesAllSessions()
    {
        var user = User.Register("test@example.com", "testuser", "hash", Role.Create("User"));
        user.AddSession("refresh1", TimeSpan.FromHours(1), "device", "127.0.0.1");
        user.AddSession("refresh2", TimeSpan.FromHours(1), "device", "127.0.0.1");
        user.BeginPasswordReset();
        var token = user.Credentials.PasswordResetToken!;

        user.ResetPassword(token, "new_hash");

        user.Credentials.PasswordHash.Should().Be("new_hash");
        user.RefreshTokens.Should().OnlyContain(t => !t.IsActive);
    }

    [Fact]
    public void User_ChangePassword_UpdatesCredentials()
    {
        var user = User.Register("test@example.com", "testuser", "old_hash", Role.Create("User"));

        user.ChangePassword("new_hash");

        user.Credentials.PasswordHash.Should().Be("new_hash");
    }
}
