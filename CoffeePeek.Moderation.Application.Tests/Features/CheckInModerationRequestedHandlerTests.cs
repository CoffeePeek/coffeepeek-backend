using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class CheckInModerationRequestedHandlerTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static ModerationReview Snapshot(string text = "Y") => ModerationReview.CreateCheckInSnapshot(
        Guid.NewGuid(), 3, Guid.NewGuid(), Guid.NewGuid(), "alice", text, 4, 5, 3, null, DateTime.UtcNow, []);

    [Fact]
    public void Snapshot_ShortTextIsValid_AndContentIsImmutable()
    {
        var snapshot = Snapshot(" Y ");
        snapshot.Comment.Should().Be("Y");
        snapshot.ContentRevision.Should().Be(3);
        snapshot.DecisionSequence.Should().Be(0);
        typeof(ModerationReview).GetMethods().Select(m => m.Name)
            .Should().NotContain(["UpdateComment", "UpdateHeader", "SetDrink", "ReplacePhotos", "Resubmit", "Create", "CreateLegacyImport"]);
        typeof(Rating).GetMethods().Select(m => m.Name).Should().NotContain("UpdateRating");
    }

    [Theory]
    [InlineData("Y", 0, 3, 3)]
    [InlineData("Y", 3, 6, 3)]
    [InlineData("Y", 3, 3, 0)]
    [InlineData("Y", null, 3, 3)]
    [InlineData("Y", 3, null, 3)]
    [InlineData("Y", 3, 3, null)]
    public void Snapshot_RejectsInvalidOrPartialRating(string text, int? place, int? service, int? coffee)
    {
        Action create = () => ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(),
            Guid.NewGuid(), "alice", text, place, service, coffee, null, DateTime.UtcNow, []);
        create.Should().Throw<DomainException>();
    }

    [Fact]
    public void Snapshot_AcceptsMaximumTextLength_AndRejectsLongerContent()
    {
        Snapshot(new string('x', 1000)).Comment.Should().HaveLength(1000);
        Action tooLong = () => Snapshot(new string('x', 1001));
        tooLong.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task LinkedDecisions_UseIncreasingSequence_AndSameStatusHasNoEffects()
    {
        var snapshot = Snapshot();
        var repository = new Mock<IModerationReviewRepository>();
        repository.Setup(r => r.GetById(snapshot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var audit = new Mock<IModerationAuditLogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var moderatorId = Guid.NewGuid();

        async Task<object?> Decide(ModerationStatus status) => (await ChangeStatusModerationReviewHandler.Handle(
            new ChangeStatusModerationReviewCommand(moderatorId, snapshot.Id, status, "Reason", null),
            repository.Object, audit.Object, unitOfWork.Object, _ct)).Item2;

        var approved = (CheckInModerationDecidedEvent)(await Decide(ModerationStatus.Approved))!;
        (await Decide(ModerationStatus.Approved)).Should().BeNull();
        var rejected = (CheckInModerationDecidedEvent)(await Decide(ModerationStatus.Rejected))!;
        (await Decide(ModerationStatus.Rejected)).Should().BeNull();
        var pending = (CheckInModerationDecidedEvent)(await Decide(ModerationStatus.Pending))!;
        (await Decide(ModerationStatus.Pending)).Should().BeNull();
        var reapproved = (CheckInModerationDecidedEvent)(await Decide(ModerationStatus.Approved))!;

        new[] { approved.DecisionSequence, rejected.DecisionSequence, pending.DecisionSequence, reapproved.DecisionSequence }
            .Should().Equal(1, 2, 3, 4);
        approved.CheckInId.Should().Be(snapshot.CheckInId);
        approved.ContentRevision.Should().Be(3);
        approved.SubmissionId.Should().Be(snapshot.Id);
        rejected.RejectionReason.Should().Be("Reason");
        reapproved.RejectionReason.Should().BeNull();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(4));
        audit.Verify(a => a.AddAsync(It.IsAny<CoffeePeek.Moderation.Domain.Entities.ModerationAuditLog>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task TelegramDecision_CannotReverseAnAlreadyDecidedCheckIn()
    {
        var snapshot = Snapshot();
        snapshot.Approve(Guid.NewGuid());
        var repository = new Mock<IModerationReviewRepository>();
        repository.Setup(r => r.GetById(snapshot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var audit = new Mock<IModerationAuditLogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var (response, outbound) = await ChangeStatusModerationReviewHandler.Handle(
            new ChangeStatusModerationReviewCommand(Guid.NewGuid(), snapshot.Id, ModerationStatus.Rejected,
                null, "Отклонено модератором Telegram", true),
            repository.Object, audit.Object, unitOfWork.Object, _ct);

        response.IsSuccess.Should().BeFalse();
        outbound.Should().BeNull();
        snapshot.ModerationStatus.Should().Be(CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus.Approved);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(a => a.AddAsync(It.IsAny<CoffeePeek.Moderation.Domain.Entities.ModerationAuditLog>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
