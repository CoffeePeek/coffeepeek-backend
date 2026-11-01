using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public sealed class SessionLifetimeTests
{
    private static User CreateUser() => User.CreateExternal("session@example.com", "Google", "subject");
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(90);

    [Fact]
    public void NewSession_Has90DaysIdleAnd365DaysAbsoluteLifetime()
    {
        var now = DateTime.UtcNow;
        var token = CreateUser().AddSession("token", IdleLifetime, "device", "ip", nowUtc: now);

        token.ExpiryDate.Should().Be(now.AddDays(90));
        token.SessionExpiresAtUtc.Should().Be(now.AddDays(365));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(89)]
    public void Rotation_AfterInactivity_ExtendsIdleLifetimeFromRefresh(int days)
    {
        var user = CreateUser();
        var start = DateTime.UtcNow.AddDays(-days);
        var original = user.AddSession("original", IdleLifetime, "device", "ip", nowUtc: start);
        var now = start.AddDays(days);

        var status = user.RotateRefreshToken("original", "rotated", IdleLifetime, "device", "ip", now);

        status.Should().Be(RefreshTokenRotationStatus.Success);
        original.IsRevoked.Should().BeTrue();
        var rotated = user.RefreshTokens.Single(t => t.Token == "rotated");
        rotated.ExpiryDate.Should().Be(now.AddDays(90));
        rotated.SessionExpiresAtUtc.Should().Be(start.AddDays(365));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(100)]
    public void IdleExpiration_RejectsRefreshWithoutRevokingOtherDevice(int days)
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;
        user.AddSession("expired", IdleLifetime, "old-device", "ip", nowUtc: now.AddDays(-days));
        var other = user.AddSession("other", IdleLifetime, "other-device", "ip");

        var status = user.RotateRefreshToken("expired", "new", IdleLifetime, "old-device", "ip", now);

        status.Should().Be(RefreshTokenRotationStatus.Expired);
        other.IsActive.Should().BeTrue();
        user.RefreshTokens.Should().HaveCount(2);
    }

    [Fact]
    public void RepeatedRotation_CannotExtendSessionBeyond365Days()
    {
        var user = CreateUser();
        var start = DateTime.UtcNow.AddDays(-365);
        user.AddSession("day-0", IdleLifetime, "device", "ip", nowUtc: start);
        var previous = "day-0";
        foreach (var day in new[] { 80, 160, 240, 320, 364 })
        {
            var next = $"day-{day}";
            user.RotateRefreshToken(previous, next, IdleLifetime, "device", "ip", start.AddDays(day))
                .Should().Be(RefreshTokenRotationStatus.Success);
            previous = next;
        }
        var last = user.RefreshTokens.Single(t => t.Token == previous);
        last.ExpiryDate.Should().Be(start.AddDays(365));
        user.RefreshTokens.Should().OnlyContain(t => t.SessionExpiresAtUtc == start.AddDays(365));
        var other = user.AddSession("other-device", IdleLifetime, "other", "ip");

        user.RotateRefreshToken(previous, "too-late", IdleLifetime, "device", "ip", start.AddDays(365))
            .Should().Be(RefreshTokenRotationStatus.Expired);

        other.IsActive.Should().BeTrue();
        user.RefreshTokens.Should().NotContain(t => t.Token == "too-late");
    }

    [Fact]
    public void ReusedUnexpiredToken_StillRevokesSessions()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;
        user.AddSession("original", IdleLifetime, "device", "ip", nowUtc: now);
        user.RotateRefreshToken("original", "rotated", IdleLifetime, "device", "ip", now);
        user.AddSession("other", IdleLifetime, "other-device", "ip");

        user.RotateRefreshToken("original", "replay", IdleLifetime, "device", "ip", now)
            .Should().Be(RefreshTokenRotationStatus.Reused);

        user.RefreshTokens.Should().OnlyContain(t => t.IsRevoked);
    }
}
