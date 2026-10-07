using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests;

public class ModerationReviewTests
{
    private static ModerationReview Snapshot(string text = "A complete check-in") =>
        ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "user", text, 3, 3, 3, null, DateTime.UtcNow, []);

    [Fact]
    public void Snapshot_PreservesIdentityRevisionAndVisitTime()
    {
        var id = Guid.NewGuid();
        var drink = new CoffeePeek.Shared.Kernel.Drinks.DrinkSelection("latte", null, "Латте", "Latte");
        var visitedAt = DateTime.UtcNow.AddDays(-1);
        var snapshot = ModerationReview.CreateCheckInSnapshot(id, 4, Guid.NewGuid(), Guid.NewGuid(),
            "user", " Y ", 5, 4, 3, drink, visitedAt, []);

        snapshot.CheckInId.Should().Be(id);
        snapshot.ContentRevision.Should().Be(4);
        snapshot.VisitedAtUtc.Should().Be(visitedAt);
        snapshot.DrinkSlug.Should().Be(drink.Slug);
        snapshot.DrinkNameRu.Should().Be(drink.NameRu);
        snapshot.DrinkNameEn.Should().Be(drink.NameEn);
        snapshot.Comment.Should().Be("Y");
        snapshot.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Snapshot_InvalidIdentityOrRevision_ThrowsDomainException(int invalidField)
    {
        Action create = () => ModerationReview.CreateCheckInSnapshot(
            invalidField == 0 ? Guid.Empty : Guid.NewGuid(), invalidField == 1 ? 0 : 1,
            invalidField == 2 ? Guid.Empty : Guid.NewGuid(), invalidField == 3 ? Guid.Empty : Guid.NewGuid(),
            "user", "Y", 3, 3, 3, null, DateTime.UtcNow, []);
        create.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Snapshot_BlankText_ThrowsDomainException(string? text)
    {
        Action create = () => Snapshot(text!);
        create.Should().Throw<DomainException>();
    }

    [Fact]
    public void Snapshot_MissingVisitTime_ThrowsDomainException()
    {
        Action create = () => ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "user", "Y", 3, 3, 3, null, default, []);
        create.Should().Throw<DomainException>();
    }

    [Fact]
    public void Snapshot_UnlinkedOrForeignPhoto_ThrowsDomainException()
    {
        var author = Guid.NewGuid();
        Action unlinked = () => ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, author, Guid.NewGuid(),
            "user", "Y", 3, 3, 3, null, DateTime.UtcNow,
            [PhotoMetadata.Create("a.jpg", "image/jpeg", "a", 10, author, null)]);
        Action foreign = () => ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, author, Guid.NewGuid(),
            "user", "Y", 3, 3, 3, null, DateTime.UtcNow,
            [PhotoMetadata.CreateCheckInPhoto(Guid.NewGuid(), 0, "a.jpg", "image/jpeg", "a", 10, Guid.NewGuid())]);
        unlinked.Should().Throw<DomainException>();
        foreign.Should().Throw<DomainException>();
    }

    [Fact]
    public void Approve_WhenPending_RecordsModeratorAndIncrementsSequence()
    {
        var snapshot = Snapshot();
        var moderator = Guid.NewGuid();
        snapshot.Approve(moderator).Should().BeTrue();
        snapshot.ModerationStatus.Should().Be(ModerationStatus.Approved);
        snapshot.ModeratedBy.Should().Be(moderator);
        snapshot.ModeratedAt.Should().NotBeNull();
        snapshot.DecisionSequence.Should().Be(1);
    }

    [Fact]
    public void Approve_WhenAlreadyApproved_HasNoEffects()
    {
        var snapshot = Snapshot();
        var moderator = Guid.NewGuid();
        snapshot.Approve(moderator);
        var decidedAt = snapshot.ModeratedAt;
        snapshot.Approve(Guid.NewGuid()).Should().BeFalse();
        snapshot.ModeratedAt.Should().Be(decidedAt);
        snapshot.ModeratedBy.Should().Be(moderator);
        snapshot.DecisionSequence.Should().Be(1);
    }

    [Fact]
    public void Reject_RecordsTrimmedReasonAndModerator()
    {
        var snapshot = Snapshot();
        var moderator = Guid.NewGuid();
        snapshot.Reject(" Inappropriate content ", moderator).Should().BeTrue();
        snapshot.ModerationStatus.Should().Be(ModerationStatus.Rejected);
        snapshot.RejectedReason.Should().Be("Inappropriate content");
        snapshot.ModeratedBy.Should().Be(moderator);
        snapshot.DecisionSequence.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_BlankReason_ThrowsDomainException(string? reason)
    {
        var snapshot = Snapshot();
        Action reject = () => snapshot.Reject(reason!, Guid.NewGuid());
        reject.Should().Throw<DomainException>();
        snapshot.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Fact]
    public void Reject_TooLongReason_ThrowsDomainException()
    {
        Action reject = () => Snapshot().Reject(new string('x', BusinessConstants.MaxRejectReasonCommentLength + 1), Guid.NewGuid());
        reject.Should().Throw<DomainException>();
    }

    [Fact]
    public void MoveToPending_ClearsReasonAndRecordsModerator()
    {
        var snapshot = Snapshot();
        snapshot.Reject("Inappropriate content", Guid.NewGuid());
        var moderator = Guid.NewGuid();
        snapshot.MoveToPending(moderator).Should().BeTrue();
        snapshot.ModerationStatus.Should().Be(ModerationStatus.Pending);
        snapshot.RejectedReason.Should().BeNull();
        snapshot.ModeratedBy.Should().Be(moderator);
        snapshot.DecisionSequence.Should().Be(2);
    }

    [Fact]
    public void Decisions_RequireModeratorIdentity()
    {
        var snapshot = Snapshot();
        Action approve = () => snapshot.Approve(Guid.Empty);
        Action reject = () => snapshot.Reject("Reason", Guid.Empty);
        Action pending = () => snapshot.MoveToPending(Guid.Empty);
        approve.Should().Throw<DomainException>();
        reject.Should().Throw<DomainException>();
        pending.Should().Throw<DomainException>();
    }
}
