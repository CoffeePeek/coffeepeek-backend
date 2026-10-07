using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;
using Moq;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Tests.Features.Review;

public class ReviewReportHandlerTests
{
    private readonly Mock<IReviewReportRepository> _reports = new();
    private readonly Mock<IQueryCheckInRepository> _checkIns = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static DomainCheckIn PublishedCheckIn()
    {
        var checkIn = DomainCheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "tester", "comment text body",
            Rating.Create(5, 5, 5), DateTime.UtcNow, CheckInVisibility.Public);
        checkIn.ApplyModerationDecision(checkIn.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow);
        return checkIn;
    }

    [Fact]
    public async Task Submit_PersistsTextReporterAndCanonicalTarget()
    {
        var checkIn = PublishedCheckIn();
        var user = Guid.NewGuid();
        _checkIns.Setup(x => x.GetTrackedByIdAsync(checkIn.Id, _ct)).ReturnsAsync(checkIn);
        var result = await SubmitReviewReportHandler.Handle(new(checkIn.Id, user, "  abuse  "),
            _reports.Object, _checkIns.Object, _uow.Object, _ct);
        result.Data.Text.Should().Be("abuse");
        result.Data.ReportedByUserId.Should().Be(user);
        result.Data.CheckInId.Should().Be(checkIn.Id);
        _reports.Verify(x => x.Add(It.Is<ReviewReport>(r => r.CheckInId == checkIn.Id)), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
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
        var checkIn = PublishedCheckIn();
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
        _checkIns.Setup(x => x.GetTrackedByIdAsync(checkIn.Id, _ct)).ReturnsAsync(state == "missing" ? null : checkIn);
        var act = () => SubmitReviewReportHandler.Handle(new(checkIn.Id, Guid.NewGuid(), "abuse"),
            _reports.Object, _checkIns.Object, _uow.Object, _ct);
        await act.Should().ThrowAsync<NotFoundException>();
        _reports.Verify(x => x.Add(It.IsAny<ReviewReport>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        await ResolveReviewReportHandler.Handle(command, _reports.Object, _checkIns.Object, _uow.Object, _cache.Object, _ct);
        await ResolveReviewReportHandler.Handle(command, _reports.Object, _checkIns.Object, _uow.Object, _cache.Object, _ct);
        checkIn.DeletedAtUtc.HasValue.Should().Be(delete);
        report.Status.Should().Be(delete ? ReviewReportStatus.CheckInDeleted : ReviewReportStatus.Dismissed);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(x => x.RemoveAsync(CacheKey.Shop.Detail(checkIn.ShopId)), delete ? Times.Exactly(2) : Times.Never());
        _cache.Verify(x => x.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct), delete ? Times.Exactly(2) : Times.Never());
        var conflicting = () => ResolveReviewReportHandler.Handle(command with { DeleteCheckIn = !delete },
            _reports.Object, _checkIns.Object, _uow.Object, _cache.Object, _ct);
        await conflicting.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Resolve_RetryRecoversCacheInvalidationAfterCommittedDeletion()
    {
        var checkIn = PublishedCheckIn();
        var report = ReviewReport.Create(checkIn.Id, Guid.NewGuid(), "abuse");
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(checkIn.Id, _ct)).ReturnsAsync(checkIn);
        _cache.SetupSequence(x => x.RemoveAsync(CacheKey.Shop.Detail(checkIn.ShopId)))
            .ThrowsAsync(new InvalidOperationException("cache unavailable"))
            .Returns(Task.CompletedTask);
        var command = new ResolveReviewReportCommand(report.Id, Guid.NewGuid(), true);
        var first = () => ResolveReviewReportHandler.Handle(command, _reports.Object, _checkIns.Object, _uow.Object, _cache.Object, _ct);
        await first.Should().ThrowAsync<InvalidOperationException>();
        var deletedAt = checkIn.DeletedAtUtc;
        await ResolveReviewReportHandler.Handle(command, _reports.Object, _checkIns.Object, _uow.Object, _cache.Object, _ct);
        checkIn.DeletedAtUtc.Should().Be(deletedAt);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(x => x.RemoveAsync(CacheKey.Shop.Detail(checkIn.ShopId)), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalComplaint_UnavailableTargetRetainsAuditAndHasNoPublicContent(bool privateTarget)
    {
        var checkIn = PublishedCheckIn();
        checkIn.ChangeVisibility(CheckInVisibility.Private);
        var targetId = checkIn.Id;
        var reporter = Guid.NewGuid();
        var report = ReviewReport.Create(targetId, reporter, "original historical complaint");
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        _checkIns.Setup(x => x.GetTrackedByIdAsync(targetId, _ct)).ReturnsAsync(privateTarget ? checkIn : null);
        var detail = await GetReviewReportHandler.Handle(new(report.Id), _reports.Object, _checkIns.Object, _ct);
        detail.Data.CheckIn.Should().BeNull();
        detail.Data.Report.CheckInId.Should().Be(targetId);
        detail.Data.Report.Text.Should().Be("original historical complaint");
        await ResolveReviewReportHandler.Handle(new(report.Id, Guid.NewGuid(), true),
            _reports.Object, _checkIns.Object, _uow.Object, _cache.Object, _ct);
        report.CheckInId.Should().Be(targetId);
        report.ReportedByUserId.Should().Be(reporter);
        checkIn.DeletedAtUtc.Should().BeNull();
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
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
