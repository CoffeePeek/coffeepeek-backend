using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public partial record UserCredential
{
    public Email Email { get; private set; }
    public string PasswordHash { get; init; }
    public bool EmailConfirmed { get; private set; }
    public string? OAuthProvider { get; private set; }
    public string? ProviderId { get; private set; }
    public string? EmailConfirmationToken { get; private set; }
    public DateTime? EmailConfirmationExpiresAt { get; private set; }
    public string? PasswordResetToken { get; private set; }
    public DateTime? PasswordResetExpiresAt { get; private set; }

    public bool HasPasswordAuth => !string.IsNullOrEmpty(PasswordHash);

    public static void EnsureValidNewPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new DomainException("Password must be at least 8 characters long");
    }

    private UserCredential()
    {
    }

    public static UserCredential CreateBasic(Email email, string passwordHash, string token) => new()
    {
        Email = email,
        PasswordHash = passwordHash,
        EmailConfirmationToken = token,
        EmailConfirmationExpiresAt = DateTime.UtcNow.AddMinutes(10)
    };

    public static UserCredential CreateExternal(Email email, string provider, string providerId) => new()
    {
        Email = email,
        OAuthProvider = provider,
        ProviderId = providerId,
        EmailConfirmed = true,
        PasswordHash = string.Empty
    };

    public void UpdateEmail(string invalidEmail)
    {
        Email = Email.Create(invalidEmail);

        ResetEmailConfirmedFlow();
    }

    public void ResetEmailConfirmedFlow()
    {
        var token = Guid.NewGuid().ToString("N");
        EmailConfirmed = false;
        EmailConfirmationToken =  token;
        EmailConfirmationExpiresAt = DateTime.UtcNow.AddMinutes(10);
    }
    
    public UserCredential ConfirmEmail(string token)
    {
        if (token != EmailConfirmationToken) throw new DomainException("Invalid token.");
        if (DateTime.UtcNow > EmailConfirmationExpiresAt) throw new DomainException("Token expired.");

        return this with
        {
            EmailConfirmed = true,
            EmailConfirmationToken = null,
            EmailConfirmationExpiresAt = null
        };
    }

    public void BeginPasswordReset()
    {
        EnsurePasswordAuthAvailable();
        PasswordResetToken = Guid.NewGuid().ToString("N");
        PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(30);
    }

    public UserCredential ChangePassword(string newHash)
    {
        EnsurePasswordAuthAvailable();

        return this with
        {
            PasswordHash = newHash,
            PasswordResetToken = null,
            PasswordResetExpiresAt = null
        };
    }

    public UserCredential CompletePasswordReset(string token, string newHash)
    {
        EnsurePasswordAuthAvailable();

        if (token != PasswordResetToken) throw new DomainException("Invalid token.");
        if (DateTime.UtcNow > PasswordResetExpiresAt) throw new DomainException("Token expired.");

        return this with
        {
            PasswordHash = newHash,
            PasswordResetToken = null,
            PasswordResetExpiresAt = null
        };
    }
    
    public bool ValidatePassword(string password, IPasswordHasherService passwordHasher)
    {
        if (!HasPasswordAuth)
            return false;

        return passwordHasher.VerifyPassword(PasswordHash, password);
    }

    private void EnsurePasswordAuthAvailable()
    {
        if (!HasPasswordAuth)
            throw new DomainException("Password login not available");
    }
}
