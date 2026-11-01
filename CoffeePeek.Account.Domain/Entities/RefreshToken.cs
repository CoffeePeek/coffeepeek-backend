using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Account.Domain.Entities;

public sealed class RefreshToken : Entity<Guid>
{
    public string Token { get; private set; }
    public DateTime ExpiryDate { get; private set; }
    public DateTime SessionExpiresAtUtc { get; private set; }
    public bool IsRevoked { get; private set; }
    public string DeviceName { get; private set; }
    public string IpAddress { get; private set; }
    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private RefreshToken() { }

    internal RefreshToken(Guid userId, string token, TimeSpan ttl, string deviceName, string ipAddress,
        DateTime sessionExpiresAtUtc, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token is required");

        Id = Guid.NewGuid();
        UserId = userId;
        Token = token;
        SessionExpiresAtUtc = sessionExpiresAtUtc;
        ExpiryDate = nowUtc.Add(ttl) < sessionExpiresAtUtc ? nowUtc.Add(ttl) : sessionExpiresAtUtc;
        DeviceName = deviceName;
        IpAddress = ipAddress;
        IsRevoked = false;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
    

    public bool IsActive => !IsRevoked && !IsExpiredAt(DateTime.UtcNow);

    public bool IsExpiredAt(DateTime nowUtc) => nowUtc >= ExpiryDate || nowUtc >= SessionExpiresAtUtc;

    public static RefreshToken Create(Guid userId, string token, TimeSpan ttl, string deviceName, string ipAddress,
        DateTime? sessionExpiresAtUtc = null, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        return new RefreshToken(userId, token, ttl, deviceName, ipAddress,
            sessionExpiresAtUtc ?? now.AddDays(365), now);
    }

    public void Revoke()
    {
        if (IsRevoked)
            return;

        IsRevoked = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
