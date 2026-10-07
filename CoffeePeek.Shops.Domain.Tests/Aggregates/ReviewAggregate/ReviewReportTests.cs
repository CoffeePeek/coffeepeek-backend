using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.ReviewAggregate;

public class ReviewReportTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void Create_RejectsEmptyText(string? text) =>
        FluentActions.Invoking(() => ReviewReport.Create(Guid.NewGuid(), Guid.NewGuid(), text!))
            .Should().Throw<ValidationException>();

    [Fact]
    public void Create_TrimsTextAndEnforcesLimit()
    {
        var report = ReviewReport.Create(Guid.NewGuid(), Guid.NewGuid(), "  complaint  ");
        report.Text.Should().Be("complaint");
        report.Status.Should().Be(ReviewReportStatus.Pending);
        report.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        ReviewReport.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 2000)).Text.Should().HaveLength(2000);
        FluentActions.Invoking(() => ReviewReport.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 2001)))
            .Should().Throw<ValidationException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_RecordsDecisionAndMakesIdenticalRetriesIdempotent(bool delete)
    {
        var report = ReviewReport.Create(Guid.NewGuid(), Guid.NewGuid(), "complaint");
        var admin = Guid.NewGuid();
        report.Resolve(delete, admin).Should().BeTrue();
        var at = report.ResolvedAtUtc;
        report.Resolve(delete, Guid.NewGuid()).Should().BeFalse();
        report.ResolvedByAdminId.Should().Be(admin);
        report.ResolvedAtUtc.Should().Be(at);
        report.Status.Should().Be(delete ? ReviewReportStatus.CheckInDeleted : ReviewReportStatus.Dismissed);
        FluentActions.Invoking(() => report.Resolve(!delete, admin)).Should().Throw<ConflictException>();
    }

    [Fact]
    public void CheckInDeletion_IsTerminalAndDelayedApprovalCannotPublishIt()
    {
        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "tester", "comment text body",
            Rating.Create(5, 5, 5), DateTime.UtcNow, CheckInVisibility.Public);
        var revision = checkIn.ContentRevision;
        checkIn.Delete();
        var deletedAt = checkIn.DeletedAtUtc;
        checkIn.Delete();
        checkIn.DeletedAtUtc.Should().Be(deletedAt);
        checkIn.IsPubliclyVisible.Should().BeFalse();
        checkIn.ApplyModerationDecision(revision, Guid.NewGuid(), 1, CheckInModerationState.Approved,
            null, DateTime.UtcNow).Should().BeFalse();
        FluentActions.Invoking(() => checkIn.ChangeVisibility(CheckInVisibility.Public)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => checkIn.UpdateContent("restored text", Rating.Create(5, 5, 5)))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Resolve_PreservesOriginalTargetAndAuditText()
    {
        var target = Guid.NewGuid();
        var user = Guid.NewGuid();
        var report = ReviewReport.Create(target, user, "historical complaint");
        report.Resolve(true, Guid.NewGuid());
        report.CheckInId.Should().Be(target);
        report.ReportedByUserId.Should().Be(user);
        report.Text.Should().Be("historical complaint");
    }
}
