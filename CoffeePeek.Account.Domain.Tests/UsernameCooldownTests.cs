using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public sealed class UsernameCooldownTests
{
    [Fact]
    public void Rename_EnforcesSevenDaysWithoutChargingRetriesOrRejectedChanges()
    {
        var user = User.CreateExternal("cooldown@example.com", "Google", "test", "Original");
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        user.UsernameChangedAtUtc.Should().BeNull();
        user.UpdateUsername(Username.Create("Second"), now);
        user.UsernameChangedAtUtc.Should().Be(now);
        user.UpdateUsername(Username.Create("Second"), now.AddDays(1));
        user.UsernameChangedAtUtc.Should().Be(now);
        var rejected = () => user.UpdateUsername(Username.Create("Third"), now.AddDays(7).AddTicks(-1));
        rejected.Should().Throw<ConflictException>();
        user.Username.Value.Should().Be("Second");
        user.UsernameChangedAtUtc.Should().Be(now);
        user.UpdateUsername(Username.Create("Third"), now.AddDays(7));
        user.Username.Value.Should().Be("Third");
        user.UsernameChangedAtUtc.Should().Be(now.AddDays(7));
    }
}
