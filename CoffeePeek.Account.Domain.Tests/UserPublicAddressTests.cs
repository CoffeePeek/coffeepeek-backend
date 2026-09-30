using CoffeePeek.Account.Domain.Entities.PublicAddressAggregate;
using CoffeePeek.Shared.Domain.PublicAddresses;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public sealed class UserPublicAddressTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Lifecycle_AliasRestoreRetire_EndsAsOwnerlessTombstoneKeepingOnlyTheSlug()
    {
        var owner = Guid.NewGuid();
        var address = UserPublicAddress.Create(owner, "ivan-petrov", 1, Now);
        address.State.Should().Be(PublicAddressState.Canonical);
        address.OwnerId.Should().Be(owner);
        address.Revision.Should().Be(1);

        address.MakeAlias(2, Now.AddSeconds(1));
        address.State.Should().Be(PublicAddressState.Alias);
        address.OwnerId.Should().Be(owner);

        address.Restore(3, Now.AddSeconds(2));
        address.State.Should().Be(PublicAddressState.Canonical);

        address.Retire(4, Now.AddSeconds(3));
        address.State.Should().Be(PublicAddressState.Tombstone);
        address.OwnerId.Should().BeNull();
        address.Slug.Should().Be("ivan-petrov");
        address.Revision.Should().Be(4);
        address.CreatedAtUtc.Should().Be(Now);

        address.Invoking(a => a.Restore(5, Now.AddSeconds(4))).Should().Throw<InvalidOperationException>();
        address.Invoking(a => a.Retire(5, Now.AddSeconds(4))).Should().Throw<InvalidOperationException>();
        address.State.Should().Be(PublicAddressState.Tombstone);
        address.Revision.Should().Be(4);
    }

    [Fact]
    public void Create_RejectsEmptyOwnerReservedOrGuidSlugNonPositiveRevisionAndNonUtcTime()
    {
        var owner = Guid.NewGuid();
        Action noOwner = () => UserPublicAddress.Create(Guid.Empty, "ivan", 1, Now);
        Action reserved = () => UserPublicAddress.Create(owner, "admin", 1, Now);
        Action guidShaped = () => UserPublicAddress.Create(owner, Guid.NewGuid().ToString(), 1, Now);
        Action zeroRevision = () => UserPublicAddress.Create(owner, "ivan", 0, Now);
        Action localTime = () => UserPublicAddress.Create(owner, "ivan", 1, DateTime.SpecifyKind(Now, DateTimeKind.Local));

        noOwner.Should().Throw<ArgumentException>();
        reserved.Should().Throw<ArgumentException>();
        guidShaped.Should().Throw<ArgumentException>();
        zeroRevision.Should().Throw<ArgumentOutOfRangeException>();
        localTime.Should().Throw<ArgumentException>();
    }

    // Username -> slug stability rows (research spike, Pitfall 11). PublicSlug is shared and versioned:
    // a failing row means a shared-rule change that would rewrite published profile addresses.
    [Theory]
    [InlineData("ivan", "ivan")]
    [InlineData("Ivan", "ivan")]
    [InlineData("Иван", "ivan")]
    [InlineData("ivan.petrov", "ivan-petrov")]
    [InlineData("Ivan_Petrov", "ivan-petrov")]
    [InlineData("ivan__petrov", "ivan-petrov")]
    [InlineData("Алёна_Ш", "alyona-sh")]
    [InlineData("Мікалай", "mikalay")]
    [InlineData("Zoë", "zoe")]
    [InlineData("Łukasz", "lukasz")]
    [InlineData("Straße", "strasse")]
    [InlineData("İlker", "lker")]
    [InlineData("admin", "place")]
    [InlineData("settings", "place")]
    [InlineData("null", "place")]
    [InlineData("by_slug", "place")]
    [InlineData("public_addresses", "place")]
    [InlineData("日本語", "place")]
    [InlineData("Ελένη", "place")]
    [InlineData("reviews", "reviews")]
    [InlineData("user123", "user123")]
    [InlineData("ivan.petrov1985", "ivan-petrov1985")]
    public void Generate_UsernameRegression(string username, string expected)
    {
        PublicSlug.Generate(username).Should().Be(expected);
    }
}
