using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Aggregates.ModerationRoasterAggregate;

public class ModerationRoasterTests
{
    [Fact]
    public void Create_ValidName_SetsPendingStatus()
    {
        var userId = Guid.NewGuid();

        var roaster = ModerationRoaster.Create("Coffee Circus", userId, "Small-batch roaster.");

        roaster.Id.Should().NotBeEmpty();
        roaster.Name.Should().Be("Coffee Circus");
        roaster.About.Should().Be("Small-batch roaster.");
        roaster.UserId.Should().Be(userId);
        roaster.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_BlankName_Throws(string invalidName)
    {
        var act = () => ModerationRoaster.Create(invalidName, Guid.NewGuid(), null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_NameTooLong_Throws()
    {
        var name = new string('a', BusinessConstants.MaxRoasterNameLength + 1);

        var act = () => ModerationRoaster.Create(name, Guid.NewGuid(), null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Approve_UnvalidatedAddress_Throws()
    {
        var roaster = ModerationRoaster.Create("Coffee Circus", Guid.NewGuid(), null);
        roaster.SetLocation(Guid.NewGuid(), new ModerationLocation("1 Main St"));

        var act = () => roaster.Approve();

        act.Should().Throw<DomainException>();
        roaster.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Fact]
    public void Approve_ValidatedAddress_Succeeds()
    {
        var roaster = ModerationRoaster.Create("Coffee Circus", Guid.NewGuid(), null);
        roaster.SetLocation(Guid.NewGuid(), new ModerationLocation("1 Main St", 53.9m, 27.5m));

        var approved = roaster.Approve();

        approved.Should().BeTrue();
        roaster.ModerationStatus.Should().Be(ModerationStatus.Approved);
    }

    [Fact]
    public void Approve_NoAddressSubmitted_Succeeds()
    {
        var roaster = ModerationRoaster.Create("Coffee Circus", Guid.NewGuid(), null);

        var approved = roaster.Approve();

        approved.Should().BeTrue();
        roaster.ModerationStatus.Should().Be(ModerationStatus.Approved);
    }

    [Fact]
    public void Approve_AlreadyApproved_IsNoOp()
    {
        var roaster = ModerationRoaster.Create("Coffee Circus", Guid.NewGuid(), null);
        roaster.Approve();

        var approvedAgain = roaster.Approve();

        approvedAgain.Should().BeFalse();
    }

    [Fact]
    public void Reject_BlankReason_Throws()
    {
        var roaster = ModerationRoaster.Create("Coffee Circus", Guid.NewGuid(), null);

        var act = () => roaster.Reject("");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reject_ValidReason_SetsRejectedStatus()
    {
        var roaster = ModerationRoaster.Create("Coffee Circus", Guid.NewGuid(), null);

        roaster.Reject("Duplicate of an existing roaster.");

        roaster.ModerationStatus.Should().Be(ModerationStatus.Rejected);
        roaster.RejectedReason.Should().Be("Duplicate of an existing roaster.");
    }

    [Fact]
    public void Reject_AlreadyApproved_Throws()
    {
        var roaster = ModerationRoaster.Create("Coffee Circus", Guid.NewGuid(), null);
        roaster.Approve();

        var act = () => roaster.Reject("Duplicate of an existing roaster.");

        act.Should().Throw<DomainException>();
    }
}
