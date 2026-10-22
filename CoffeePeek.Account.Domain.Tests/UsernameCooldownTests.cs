using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public sealed class UsernameCooldownTests
{
    [Fact]
    public void Rename_AllowsThreeChangesAndRejectsFourthWithoutChangingState()
    {
        var user = User.CreateExternal("cooldown@example.com", "Google", "test", "Original");
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        user.UsernameChangedAtUtc.Should().BeNull();
        user.UsernameChangeHistoryUtc.Should().BeEmpty();
        user.UpdateUsername(Username.Create("Second"), now);
        user.Username.Value.Should().Be("Second");
        user.UpdateUsername(Username.Create("Third"), now);
        user.UpdateUsername(Username.Create("Fourth"), now);

        var rejected = () => user.UpdateUsername(Username.Create("Fifth"), now.AddDays(7).AddTicks(-1));

        rejected.Should().Throw<ConflictException>()
            .WithMessage($"Username can be changed 3 times every 7 days. Try again after {now.AddDays(7):O}.");
        user.Username.Value.Should().Be("Fourth");
        user.UsernameChangedAtUtc.Should().Be(now);
        user.UsernameChangeHistoryUtc.Should().Equal(now, now, now);
    }

    [Fact]
    public void Rename_SameUsernameDoesNotConsumeQuotaEvenAtLimit()
    {
        var user = User.CreateExternal("retry@example.com", "Google", "test", "Original");
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        user.UpdateUsername(Username.Create("Original"), now);
        user.UsernameChangeHistoryUtc.Should().BeEmpty();
        user.UpdateUsername(Username.Create("Second"), now);
        user.UpdateUsername(Username.Create("Second"), now.AddHours(1));
        user.UpdateUsername(Username.Create("Third"), now.AddHours(2));
        user.UpdateUsername(Username.Create("Fourth"), now.AddHours(3));

        user.UpdateUsername(Username.Create("Fourth"), now.AddDays(1));

        user.UsernameChangedAtUtc.Should().Be(now.AddHours(3));
        user.UsernameChangeHistoryUtc.Should().Equal(now, now.AddHours(2), now.AddHours(3));
    }

    [Fact]
    public void Rename_SlotsExpireIndividuallyAtExactSevenDayBoundary()
    {
        var user = User.CreateExternal("rolling@example.com", "Google", "test", "Original");
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        user.UpdateUsername(Username.Create("Second"), now);
        user.UpdateUsername(Username.Create("Third"), now.AddDays(1));
        user.UpdateUsername(Username.Create("Fourth"), now.AddDays(2));

        var beforeBoundary = () => user.UpdateUsername(Username.Create("Fifth"), now.AddDays(7).AddTicks(-1));
        beforeBoundary.Should().Throw<ConflictException>();
        user.UpdateUsername(Username.Create("Fifth"), now.AddDays(7));
        user.UsernameChangeHistoryUtc.Should().Equal(now.AddDays(1), now.AddDays(2), now.AddDays(7));

        var beforeNextBoundary = () => user.UpdateUsername(Username.Create("Sixth"), now.AddDays(8).AddTicks(-1));
        beforeNextBoundary.Should().Throw<ConflictException>()
            .WithMessage($"*{now.AddDays(8):O}*");
        user.UpdateUsername(Username.Create("Sixth"), now.AddDays(8));
        user.UsernameChangeHistoryUtc.Should().Equal(now.AddDays(2), now.AddDays(7), now.AddDays(8));
    }

    [Fact]
    public void Rename_AfterInactivityDiscardsExpiredChanges()
    {
        var user = User.CreateExternal("expired@example.com", "Google", "test", "Original");
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        user.UpdateUsername(Username.Create("Second"), now);
        user.UpdateUsername(Username.Create("Third"), now.AddDays(1));
        user.UpdateUsername(Username.Create("Fourth"), now.AddDays(2));

        user.UpdateUsername(Username.Create("Fifth"), now.AddDays(14));

        user.UsernameChangedAtUtc.Should().Be(now.AddDays(14));
        user.UsernameChangeHistoryUtc.Should().Equal(now.AddDays(14));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Rename_RejectsNonUtcTimeWithoutConsumingQuota(DateTimeKind kind)
    {
        var user = User.CreateExternal("time@example.com", "Google", "test", "Original");
        var invalidTime = new DateTime(2026, 9, 30, 12, 0, 0, kind);

        var rename = () => user.UpdateUsername(Username.Create("Second"), invalidTime);

        rename.Should().Throw<ArgumentException>();
        user.Username.Value.Should().Be("Original");
        user.UsernameChangedAtUtc.Should().BeNull();
        user.UsernameChangeHistoryUtc.Should().BeEmpty();
    }
}
