using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
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
        report.Status.Should().Be(delete ? ReviewReportStatus.ReviewDeleted : ReviewReportStatus.Dismissed);
        FluentActions.Invoking(() => report.Resolve(!delete, admin)).Should().Throw<ConflictException>();
    }

    [Fact]
    public void AdminRemoval_PreventsRestoreWhileNormalHideCanRestore()
    {
        var review = Review.Create(Guid.NewGuid(), Guid.NewGuid(), "tester", null, "comment text body", 5, 5, 5, Guid.NewGuid());
        review.Hide();
        review.Restore();
        review.IsSoftDelete.Should().BeFalse();
        review.RemoveByAdmin();
        review.Restore();
        review.IsRemovedByAdmin.Should().BeTrue();
        review.IsSoftDelete.Should().BeTrue();
    }
}
