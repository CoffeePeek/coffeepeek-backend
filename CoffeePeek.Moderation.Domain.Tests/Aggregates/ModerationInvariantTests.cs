using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.Enums;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Aggregates;

public class ModerationInvariantTests
{
    [Fact]
    public void Approval_ClearsPreviousRejection_AndRepeatedRejectionIsNoOp()
    {
        var shop = Shop();
        shop.Reject(" Incorrect address ").Should().BeTrue();
        shop.Reject("Incorrect address").Should().BeFalse();
        shop.Approve();
        shop.RejectedReason.Should().BeNull();

        var roaster = ModerationRoaster.Create("Roaster", null, null);
        roaster.Reject(" Duplicate ").Should().BeTrue();
        roaster.Reject("Duplicate").Should().BeFalse();
        roaster.Approve();
        roaster.RejectedReason.Should().BeNull();
    }

    [Fact]
    public void PublishedShop_CannotBeEditedThroughAggregate()
    {
        var shop = Shop();
        shop.Approve();
        Action update = () => shop.UpdateInfo("Renamed", null, null, null, null);
        Action photo = () => shop.AddPhoto("a.jpg", "image/jpeg", "a", 1);
        Action menu = () => shop.ReplaceMenuItems([], DateTime.UtcNow);

        update.Should().Throw<DomainException>();
        photo.Should().Throw<DomainException>();
        menu.Should().Throw<DomainException>();
        shop.AddShopId(Guid.NewGuid());
        shop.ReplaceMenuItems([], DateTime.UtcNow);
    }

    [Fact]
    public void InvalidEdit_DoesNotPartiallyRenameShop()
    {
        var shop = Shop();
        Action update = () => shop.UpdateInfo("Renamed", new string('x', 1001), null, null, null);
        update.Should().Throw<DomainException>();
        shop.Name.Should().Be("Cafe");
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public void Location_RejectsInvalidCoordinates(int latitude, int longitude)
    {
        Action create = () => new ModerationLocation("Address", latitude, longitude);
        create.Should().Throw<DomainException>();
    }

    [Fact]
    public void InvalidSchedule_DoesNotClearExistingSchedule()
    {
        var shop = Shop();
        shop.UpdateSchedules([(DayOfWeek.Monday, false, new() { (TimeSpan.FromHours(8), TimeSpan.FromHours(20)) })]);
        Action update = () => shop.UpdateSchedules([((DayOfWeek)99, true, new())]);

        update.Should().Throw<DomainException>();
        shop.Schedules.Should().ContainSingle().Which.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void InvalidContactPatch_DoesNotPartiallyChangeCandidate()
    {
        var candidate = Candidate();
        Action patch = () => candidate.PatchContacts("@newprofile", true, "new phone", true, "invalid", true, null, false);
        patch.Should().Throw<DomainException>();
        candidate.Instagram.Should().BeNull();
        candidate.Phone.Should().BeNull();
    }

    [Fact]
    public void InvalidPhotoMetadata_IsRejectedByBothSubmissionTypes()
    {
        Action shopPhoto = () => Shop().AddPhoto("a.jpg", "image/jpeg", "key", 0);
        Action roasterPhoto = () => ModerationRoaster.Create("Roaster", null, null).AddPhoto("", "image/jpeg", "key", 1);

        shopPhoto.Should().Throw<DomainException>();
        roasterPhoto.Should().Throw<DomainException>();
    }

    [Fact]
    public void InvalidImportDecision_DoesNotChangeStatus()
    {
        var candidate = Candidate();
        Action decide = () => candidate.Decide((ImportQueueStatus)99, null, [], Guid.NewGuid(), false, DateTimeOffset.UtcNow);

        decide.Should().Throw<DomainException>();
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Pending);
    }

    [Fact]
    public void ImportPublication_LocksContactChanges_AndPublishedIdentity()
    {
        var candidate = Candidate();
        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Guid.NewGuid(), false, DateTimeOffset.UtcNow);
        Action patch = () => candidate.PatchContacts(null, false, "new phone", true, null, false, null, false);
        patch.Should().Throw<DomainException>();
        var id = Guid.NewGuid();
        candidate.AttachPublishedShop(id);
        candidate.AttachPublishedShop(id);
        Action relink = () => candidate.AttachPublishedShop(Guid.NewGuid());
        relink.Should().Throw<DomainException>();
        candidate.ResultingShopId.Should().Be(id);
    }

    private static ModerationShop Shop()
    {
        var shop = ModerationShop.Create("Cafe", null, Guid.NewGuid(), null, CoffeeFocus.Cafe);
        shop.SetLocation(new ModerationLocation("Address", 53, 27));
        return shop;
    }

    private static ShopImportCandidate Candidate() => ShopImportCandidate.FromOsm(
        new("node/1", "Cafe", "Address", 53, 27, null, null, null, null, null, null, null, null,
            new Dictionary<string, string> { ["amenity"] = "cafe" }), DateTimeOffset.UtcNow);
}
