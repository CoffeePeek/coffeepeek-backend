using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using FluentAssertions;
using Moq;
using DomainReview = CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review;

namespace CoffeePeek.Shops.Application.Tests.Features.Review;

public class ReviewReportHandlerTests
{
    private readonly Mock<IReviewReportRepository> _reports = new();
    private readonly Mock<IReviewRepository> _reviews = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private static DomainReview Review() => DomainReview.Create(Guid.NewGuid(), Guid.NewGuid(), "tester", null,
        "comment text body", 5, 5, 5, Guid.NewGuid());

    [Fact]
    public async Task Submit_PersistsTextAndReporter()
    {
        var review = Review();
        var user = Guid.NewGuid();
        _reviews.Setup(x => x.GetById(review.Id, _ct)).ReturnsAsync(review);
        var result = await SubmitReviewReportHandler.Handle(new(review.Id, user, "  abuse  "),
            _reports.Object, _reviews.Object, _uow.Object, _ct);
        result.Data.Text.Should().Be("abuse");
        result.Data.ReportedByUserId.Should().Be(user);
        _reports.Verify(x => x.Add(It.Is<ReviewReport>(r => r.ReviewId == review.Id)), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_MissingOrHiddenReviewDoesNotSave(bool hidden)
    {
        var review = Review();
        review.Hide();
        _reviews.Setup(x => x.GetById(review.Id, _ct)).ReturnsAsync(hidden ? review : null);
        var act = () => SubmitReviewReportHandler.Handle(new(review.Id, Guid.NewGuid(), "abuse"),
            _reports.Object, _reviews.Object, _uow.Object, _ct);
        await act.Should().ThrowAsync<NotFoundException>();
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resolve_ChangesReviewOnlyWhenDeletingAndSavesOnce(bool delete)
    {
        var review = Review();
        var report = ReviewReport.Create(review.Id, Guid.NewGuid(), "abuse");
        _reports.Setup(x => x.GetByIdAsync(report.Id, _ct)).ReturnsAsync(report);
        _reviews.Setup(x => x.GetById(review.Id, _ct)).ReturnsAsync(review);
        var command = new ResolveReviewReportCommand(report.Id, Guid.NewGuid(), delete);
        await ResolveReviewReportHandler.Handle(command, _reports.Object, _reviews.Object, _uow.Object, _cache.Object, _ct);
        await ResolveReviewReportHandler.Handle(command, _reports.Object, _reviews.Object, _uow.Object, _cache.Object, _ct);
        review.IsSoftDelete.Should().Be(delete);
        review.IsRemovedByAdmin.Should().Be(delete);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(x => x.RemoveAsync(CacheKey.Shop.Detail(review.CoffeeShopId)), delete ? Times.Exactly(2) : Times.Never());
        _cache.Verify(x => x.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct), delete ? Times.Exactly(2) : Times.Never());
        var conflicting = () => ResolveReviewReportHandler.Handle(command with { DeleteReview = !delete },
            _reports.Object, _reviews.Object, _uow.Object, _cache.Object, _ct);
        await conflicting.Should().ThrowAsync<ConflictException>();
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
