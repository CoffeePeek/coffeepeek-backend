using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.PublicAddresses;

public sealed class PublicAddressTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("shop")]
    [InlineData("roaster")]
    [InlineData("city")]
    [InlineData("zone")]
    public void Lifecycle_AllowsRestoringOwnAliasAndPermanentlyReservesRetiredAddress(string kind)
    {
        var owner = Guid.NewGuid();
        PublicAddress address = kind switch
        {
            "shop" => CoffeeShopPublicAddress.Create(owner, "coffee", 1, Now),
            "roaster" => RoasterPublicAddress.Create(owner, "coffee", 1, Now),
            "city" => CityPublicAddress.Create(owner, "coffee", 1, Now),
            "zone" => CoffeeZonePublicAddress.Create(owner, "coffee", 1, Now),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
        address.MakeAlias(2, Now.AddSeconds(1));
        address.State.Should().Be(PublicAddressState.Alias);
        address.Restore(3, Now.AddSeconds(2));
        address.State.Should().Be(PublicAddressState.Canonical);
        address.OwnerId.Should().Be(owner);
        address.Retire(4, Now.AddSeconds(3));
        address.OwnerId.Should().BeNull();
        address.Slug.Should().Be("coffee");
        address.State.Should().Be(PublicAddressState.Tombstone);
        address.CreatedAtUtc.Should().Be(Now);
        address.Revision.Should().Be(4);
        address.Invoking(a => a.Restore(5, Now.AddSeconds(4))).Should().Throw<InvalidOperationException>();
        address.Invoking(a => a.Retire(5, Now.AddSeconds(4))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Lifecycle_RejectsInvalidTransitionsWithoutMutatingState()
    {
        var address = CoffeeShopPublicAddress.Create(Guid.NewGuid(), "coffee", 1, Now);
        address.Invoking(a => a.Restore(2, Now)).Should().Throw<InvalidOperationException>();
        address.Invoking(a => a.MakeAlias(1, Now)).Should().Throw<ArgumentOutOfRangeException>();
        address.Invoking(a => a.MakeAlias(2, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified))).Should().Throw<ArgumentException>();
        address.State.Should().Be(PublicAddressState.Canonical);
        address.Revision.Should().Be(1);
        address.MakeAlias(2, Now);
        address.Retire(3, Now);
        address.State.Should().Be(PublicAddressState.Tombstone);
    }

    [Fact]
    public void Create_RequiresOwnerValidSlugPositiveRevisionAndUtc()
    {
        var owner = Guid.NewGuid();
        Action noOwner = () => CoffeeShopPublicAddress.Create(Guid.Empty, "coffee", 1, Now);
        Action reserved = () => CoffeeShopPublicAddress.Create(owner, "admin", 1, Now);
        Action revision = () => CoffeeShopPublicAddress.Create(owner, "coffee", 0, Now);
        Action localTime = () => CoffeeShopPublicAddress.Create(owner, "coffee", 1, DateTime.SpecifyKind(Now, DateTimeKind.Local));
        noOwner.Should().Throw<ArgumentException>();
        reserved.Should().Throw<ArgumentException>();
        revision.Should().Throw<ArgumentOutOfRangeException>();
        localTime.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Transition_ClockStepsBackwards_ClampsInsteadOfThrowing()
    {
        // A backwards clock step used to throw, turning account/shop deletion into a 500 until the clock caught up.
        var address = CoffeeShopPublicAddress.Create(Guid.NewGuid(), "coffee", 1, Now);
        address.MakeAlias(2, Now.AddSeconds(10));

        address.Retire(3, Now.AddSeconds(5));

        address.State.Should().Be(PublicAddressState.Tombstone);
        address.Revision.Should().Be(3);
        address.UpdatedAtUtc.Should().Be(Now.AddSeconds(10), "timestamps stay monotonic");
    }
}
