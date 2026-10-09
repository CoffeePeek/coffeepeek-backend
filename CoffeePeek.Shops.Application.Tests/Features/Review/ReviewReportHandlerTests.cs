using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;
using Moq;
using Wolverine;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Tests.Features.Review;

public class ReviewReportHandlerTests
{
    private static readonly Guid LegacyReviewId = Guid.Parse("d59022f5-cb38-4c18-a798-b44164e17a68");
    private readonly Mock<IReviewReportRepository> _reports = new();
    private readonly Mock<IQueryCheckInRepository> _checkIns = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IMessageBus> _bus = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static DomainCheckIn PublishedCheckIn(Guid? preservedReviewId = null)
    {
        var visited = DateTime.UtcNow.AddYears(-2);
        var checkIn = DomainCheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "tester", "comment text body",
            Rating.Create(5, 5, 5), visited, CheckInVisibility.Public, id: preservedReviewId);
        checkIn.CreatedAtUtc = visited.AddHours(1);
        checkIn.ApplyModerationDecision(checkIn.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, checkIn.CreatedAtUtc);
        return checkIn;
    }

    [Fact]
    public async Task ComplaintDetailCanReadVisitWithoutRating()
    {
        var checkIn = DomainCheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "tester", null, null, DateTime.UtcNow);
        var report = ReviewReport.Create(checkIn.Id, Guid.NewGuid(), "complaint");
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(checkIn.Id, _ct)).ReturnsAsync(checkIn);
        var detail = await GetReviewReportHandler.Handle(new(report.Id), _reports.Object, _checkIns.Object, _ct);
        Assert.NotNull(detail.Data.CheckIn);
        Assert.Null(detail.Data.CheckIn.RatingPlace);
        Assert.Null(detail.Data.CheckIn.RatingService);
        Assert.Null(detail.Data.CheckIn.RatingCoffee);
    }

    private static void ChangeState(DomainCheckIn checkIn, string state)
    {
        switch (state)
        {
            case "private": checkIn.ChangeVisibility(CheckInVisibility.Private); break;
            case "pending": checkIn.ApplyModerationDecision(checkIn.ContentRevision, checkIn.ModerationSubmissionId!.Value,
                2, CheckInModerationState.Pending, null, DateTime.UtcNow); break;
            case "rejected": checkIn.ApplyModerationDecision(checkIn.ContentRevision, checkIn.ModerationSubmissionId!.Value,
                2, CheckInModerationState.Rejected, "abuse", DateTime.UtcNow); break;
            case "stale":
                var revision = checkIn.ContentRevision;
                checkIn.UpdateContent("edited comment", Rating.Create(4, 4, 4));
                checkIn.ApplyModerationDecision(revision, Guid.NewGuid(), 2, CheckInModerationState.Approved, null, DateTime.UtcNow);
                break;
            case "deleted": checkIn.Delete(); break;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_PersistsTextReporterAndCanonicalTarget(bool migrated)
    {
        var checkIn = PublishedCheckIn(migrated ? LegacyReviewId : null);
        var user = Guid.NewGuid();
        _checkIns.Setup(x => x.GetTrackedByIdAsync(checkIn.Id, _ct)).ReturnsAsync(checkIn);
        var result = await SubmitReviewReportHandler.Handle(new(checkIn.Id, user, "  abuse  "),
            _reports.Object, _checkIns.Object, _ct);
        result.Data.Text.Should().Be("abuse");
        result.Data.ReportedByUserId.Should().Be(user);
        result.Data.CheckInId.Should().Be(checkIn.Id);
        _reports.Verify(x => x.Add(It.Is<ReviewReport>(r => r.CheckInId == checkIn.Id)), Times.Once);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("private")]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("stale")]
    [InlineData("deleted")]
    public async Task Submit_UnavailableOrUnapprovedCheckInDoesNotSave(string state)
    {
        var checkIn = PublishedCheckIn(LegacyReviewId);
        ChangeState(checkIn, state);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(checkIn.Id, _ct)).ReturnsAsync(state == "missing" ? null : checkIn);
        var act = () => SubmitReviewReportHandler.Handle(new(checkIn.Id, Guid.NewGuid(), "abuse"),
            _reports.Object, _checkIns.Object, _ct);
        await act.Should().ThrowAsync<NotFoundException>();
        _reports.Verify(x => x.Add(It.IsAny<ReviewReport>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resolve_DeletesPublishedTargetOnlyOnExplicitDecisionAndSavesOnce(bool delete)
    {
        var checkIn = PublishedCheckIn();
        var report = ReviewReport.Create(checkIn.Id, Guid.NewGuid(), "abuse");
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(checkIn.Id, _ct)).ReturnsAsync(checkIn);
        var command = new ResolveReviewReportCommand(report.Id, Guid.NewGuid(), delete);
        await ResolveReviewReportHandler.Handle(command, _reports.Object, _checkIns.Object, _uow.Object, _bus.Object, _ct);
        await ResolveReviewReportHandler.Handle(command, _reports.Object, _checkIns.Object, _uow.Object, _bus.Object, _ct);
        checkIn.DeletedAtUtc.HasValue.Should().Be(delete);
        report.Status.Should().Be(delete ? ReviewReportStatus.CheckInDeleted : ReviewReportStatus.Dismissed);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == checkIn.Id && message.ShopId == checkIn.ShopId && message.UserId == checkIn.UserId)),
            delete ? Times.Once() : Times.Never());
        var conflicting = () => ResolveReviewReportHandler.Handle(command with { DeleteCheckIn = !delete },
            _reports.Object, _checkIns.Object, _uow.Object, _bus.Object, _ct);
        await conflicting.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Resolve_ReplayDoesNotRepeatCommittedDeletionOrOutboxEvent()
    {
        var checkIn = PublishedCheckIn(LegacyReviewId);
        var report = ReviewReport.Create(checkIn.Id, Guid.NewGuid(), "abuse");
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(checkIn.Id, _ct)).ReturnsAsync(checkIn);
        var command = new ResolveReviewReportCommand(report.Id, Guid.NewGuid(), true);
        await ResolveReviewReportHandler.Handle(command, _reports.Object, _checkIns.Object,
            _uow.Object, _bus.Object, _ct);
        var deletedAt = checkIn.DeletedAtUtc;
        var reportAudit = ReviewReportDto.From(report);
        await ResolveReviewReportHandler.Handle(command with { AdminId = Guid.NewGuid() },
            _reports.Object, _checkIns.Object, _uow.Object, _bus.Object, _ct);
        checkIn.DeletedAtUtc.Should().Be(deletedAt);
        ReviewReportDto.From(report).Should().Be(reportAudit);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == checkIn.Id)), Times.Once);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("stale")]
    [InlineData("deleted")]
    public async Task HistoricalComplaint_AdminDetailRetainsTargetAndOriginalAudit(string state)
    {
        var checkIn = PublishedCheckIn(LegacyReviewId);
        var report = ReviewReport.Create(LegacyReviewId, Guid.NewGuid(), "original historical complaint");
        report.CreatedAtUtc = checkIn.CreatedAtUtc.AddDays(1);
        report.Resolve(false, Guid.NewGuid());
        var audit = ReviewReportDto.From(report);
        ChangeState(checkIn, state);
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(LegacyReviewId, _ct)).ReturnsAsync(checkIn);

        var detail = await GetReviewReportHandler.Handle(new(report.Id), _reports.Object, _checkIns.Object, _ct);

        detail.Data.Report.Should().Be(audit);
        detail.Data.CheckIn.Should().Be(new ReportedCheckInDto(LegacyReviewId, checkIn.ShopId, checkIn.UserId,
            checkIn.Username, checkIn.Note, checkIn.Rating.Place, checkIn.Rating.Service, checkIn.Rating.Coffee,
            checkIn.VisitedAt, checkIn.DeletedAtUtc, checkIn.IsPubliclyVisible));
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HistoricalComplaint_MissingTargetRetainsAuditAndCanBeResolved()
    {
        var report = ReviewReport.Create(LegacyReviewId, Guid.NewGuid(), "original historical complaint");
        var audit = ReviewReportDto.From(report);
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        var detail = await GetReviewReportHandler.Handle(new(report.Id), _reports.Object, _checkIns.Object, _ct);
        detail.Data.Report.Should().Be(audit);
        detail.Data.CheckIn.Should().BeNull();

        await ResolveReviewReportHandler.Handle(new(report.Id, Guid.NewGuid(), true),
            _reports.Object, _checkIns.Object, _uow.Object, _bus.Object, _ct);
        report.CheckInId.Should().Be(LegacyReviewId);
        report.ReportedByUserId.Should().Be(audit.ReportedByUserId);
        report.Text.Should().Be(audit.Text);
        report.CreatedAtUtc.Should().Be(audit.CreatedAtUtc);
        report.Status.Should().Be(ReviewReportStatus.CheckInDeleted);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("stale")]
    [InlineData("deleted")]
    public async Task Resolve_RemovesExactHistoricalTargetEvenWhenNoLongerPublicAndCannotRepublish(string state)
    {
        var checkIn = PublishedCheckIn(LegacyReviewId);
        var other = DomainCheckIn.CreateUnified(checkIn.UserId, checkIn.ShopId, "tester", "another visit",
            Rating.Create(2, 3, 4), checkIn.VisitedAt.AddDays(-1), CheckInVisibility.Public);
        other.ApplyModerationDecision(other.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow);
        var oldRevision = checkIn.ContentRevision;
        var oldSubmission = checkIn.ModerationSubmissionId!.Value;
        var report = ReviewReport.Create(LegacyReviewId, Guid.NewGuid(), "original complaint");
        ChangeState(checkIn, state);
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(LegacyReviewId, _ct)).ReturnsAsync(checkIn);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(other.Id, _ct)).ReturnsAsync(other);
        var command = new ResolveReviewReportCommand(report.Id, Guid.NewGuid(), true);

        await ResolveReviewReportHandler.Handle(command, _reports.Object, _checkIns.Object, _uow.Object, _bus.Object, _ct);
        checkIn.DeletedAtUtc.Should().NotBeNull();
        var deletedAt = checkIn.DeletedAtUtc;
        var reportAudit = ReviewReportDto.From(report);
        await ResolveReviewReportHandler.Handle(command with { AdminId = Guid.NewGuid() },
            _reports.Object, _checkIns.Object, _uow.Object, _bus.Object, _ct);

        checkIn.DeletedAtUtc.Should().Be(deletedAt);
        checkIn.ApplyModerationDecision(oldRevision, oldSubmission, 99,
            CheckInModerationState.Approved, null, DateTime.UtcNow).Should().BeFalse();
        checkIn.IsPubliclyVisible.Should().BeFalse();
        other.IsPubliclyVisible.Should().BeTrue();
        other.DeletedAtUtc.Should().BeNull();
        ReviewReportDto.From(report).Should().Be(reportAudit);
        _checkIns.Verify(x => x.GetTrackedByIdAsync(other.Id, It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == checkIn.Id)), Times.Once);
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 101, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 20, 99)]
    [InlineData(int.MaxValue, 100, 0)]
    public async Task List_RejectsInvalidPaginationAndStatus(int page, int pageSize, int status)
    {
        var act = () => GetReviewReportsHandler.Handle(new((ReviewReportStatus)status, page, pageSize), _reports.Object, _ct);
        await act.Should().ThrowAsync<ValidationException>();
        _reports.Verify(x => x.GetPageAsync(It.IsAny<ReviewReportStatus?>(), It.IsAny<int>(), It.IsAny<int>(), _ct), Times.Never);
    }
}
