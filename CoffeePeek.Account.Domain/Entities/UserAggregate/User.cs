using System.Text.RegularExpressions;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Events;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public class User : AggregateRoot<Guid>
{
    public Username Username { get; private set; }
    public PhoneNumber? PhoneNumber { get; private set; }

    public UserCredential Credentials { get; private set; }
    public UserStatistics Statistics { get; private set; }

    public string? About { get; private set; }
    public Guid? PhotoMetadataId { get; private set; }
    public PhotoMetadata? PhotoMetadata { get; private set; }
    public bool IsSoftDelete { get; private set; }
    public bool IsBlocked { get; private set; }

    private readonly List<RefreshToken> _refreshTokens = [];
    public ICollection<RefreshToken> RefreshTokens => _refreshTokens;

    private readonly IList<Role> _roles = [];
    public ICollection<Role> Roles => _roles;
    
    // ReSharper disable once UnusedMember.Local
    private User() { }

    public static User Register(string invalidEmail, string invalidUsername, string passwordHash, Role defaultRole)
    {
        Email email;
        Username userName;
        try
        {
            email = Email.Create(invalidEmail);
            userName = Username.Create(invalidUsername);
        }
        catch (DomainException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            throw new DomainException("Invalid email or username");
        }
        
        var userId = Guid.NewGuid();
        var token = Guid.NewGuid().ToString("N");

        var user = new User
        {
            Id = userId,
            Username = userName,
            Credentials = UserCredential.CreateBasic(email, passwordHash, token),
            Statistics = UserStatistics.Empty()
        };

        user.AssignRole(defaultRole);
        
        user.AddDomainEvent(new UserRegisteredInternalEvent(userId, email.Value, userName, token));
        
        return user;
    }

    public static User CreateExternal(string invalidEmail, string provider, string providerId, string? usernameOverride = null)
    {
        var email = Email.Create(invalidEmail);
        var userId = Guid.NewGuid();
        var username = usernameOverride is null
            ? SanitizeUsernameFromEmail(email.Value)
            : usernameOverride;
        return new User
        {
            Id = userId,
            Username = Username.Create(username),
            Credentials = UserCredential.CreateExternal(email, provider, providerId),
            Statistics = UserStatistics.Empty()
        };
    }

    private static string SanitizeUsernameFromEmail(string email)
    {
        var local = email.Split('@')[0];
        // Keep only chars valid for Username: letters, digits, dots, underscores
        var sanitized = Regex.Replace(local, @"[^\p{L}\p{Nd}._]", "");
        // Must start with a letter
        if (sanitized.Length == 0 || !char.IsLetter(sanitized[0]))
            sanitized = "user" + sanitized;
        // Enforce min/max length
        if (sanitized.Length < 3)
            sanitized = sanitized.PadRight(3, '0');
        if (sanitized.Length > 30)
            sanitized = sanitized[..30];
        return sanitized;
    }

    public void AddSession(string token, TimeSpan ttl, string device, string ip)
    {
        if (_refreshTokens.Count(t => t.IsActive) >= BusinessConstants.MaxActiveSessions)
        {
            _refreshTokens.Where(t => t.IsActive).OrderBy(t => t.CreatedAtUtc).First().Revoke();
        }

        _refreshTokens.Add(new RefreshToken(Id, token, ttl, device, ip));
    }

    public void RotateRefreshToken(string oldRefreshToken, string newRefreshToken, TimeSpan ttl, string device, string ip)
    {
        var existing = _refreshTokens.FirstOrDefault(t => t.Token == oldRefreshToken);
        if (existing is not { IsActive: true })
        {
            RevokeAllSessions();
            throw new DomainException("Security breach: Token reused.");
        }

        existing.Revoke();
        AddSession(newRefreshToken, ttl, device, ip);
    }

    public void RevokeAllSessions() => _refreshTokens.ForEach(t => t.Revoke());

    public bool RevokeSession(Guid sessionId)
    {
        var session = _refreshTokens.FirstOrDefault(t => t.Id == sessionId);
        if (session is null)
            return false;

        session.Revoke();
        return true;
    }

    public void Logout(string tokenValue) =>
        _refreshTokens.FirstOrDefault(t => t.Token == tokenValue)?.Revoke();

    public void AssignRole(Role role)
    {
        if (_roles.Any(r => r.Id == role.Id)) return;
        _roles.Add(role);
    }

    public void ReplaceRoles(Role role)
    {
        _roles.Clear();
        _roles.Add(role);
    }

    public void UpdateAbout(string about)
    {
        if (about != About)
        {
            About = about;
        }
    }

    public void SetSoftDelete()
    {
        IsSoftDelete = true;
    }

    public void SetBlocked(bool blocked)
    {
        IsBlocked = blocked;
        if (blocked)
            RevokeAllSessions();
    }

    public void EnsureCanAuthenticate()
    {
        if (IsSoftDelete || IsBlocked)
            throw new DomainException("Account is disabled.");
    }

    public void UpdateAvatar(PhotoMetadata photoMetadata)
    {
        PhotoMetadata = photoMetadata;
        PhotoMetadataId = photoMetadata.Id;
    }
    
    public void UpdatePhoneNumber(PhoneNumber phoneNumber)
    {
        if (phoneNumber != PhoneNumber)
        {
            PhoneNumber = phoneNumber;
        }
    }
    
    public void UpdateUsername(Username userName)
    {
        if (userName != Username)
        {
            Username = userName;
        }
    }

    public void ConfirmEmail(string token)
    {
        if (Credentials.EmailConfirmed)
            throw new DomainException("Email already confirmed.");

        Credentials = Credentials.ConfirmEmail(token);
        AddDomainEvent(new EmailConfirmedInternalEvent());
    }

    public void ChangePassword(string newHash)
    {
        Credentials = Credentials.ChangePassword(newHash);
        RevokeAllSessions();
    }

    public void BeginPasswordReset()
    {
        Credentials.BeginPasswordReset();
    }

    public void ResetPassword(string token, string newHash)
    {
        Credentials = Credentials.CompletePasswordReset(token, newHash);
        RevokeAllSessions();
    }
}
