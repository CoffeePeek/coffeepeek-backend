using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Review.SetReviewHelpful;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Review.SetReviewHelpful;

public class SetReviewHelpfulHandlerTests
{
    private readonly Mock<IReviewRepository> _reviewRepoMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly SetReviewHelpfulHandler _handler = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static Domain.Aggregates.ReviewAggregate.Review CreateReview(Guid ownerId)
    {
        return Domain.Aggregates.ReviewAggregate.Review.Create(
            Guid.NewGuid(), ownerId, "tester", "header text", "comment text body", 5, 5, 5, Guid.NewGuid());
    }

    [Fact]
    public async Task Handle_WhenMarkingHelpful_ReturnsCountAndInvalidatesShopCacheOnChange()
    {
        var review = CreateReview(Guid.NewGuid());
        var voterId = Guid.NewGuid();
        _reviewRepoMock.Setup(r => r.GetById(review.Id, _ct)).ReturnsAsync(review);
        _reviewRepoMock.Setup(r => r.SetHelpfulAsync(review.Id, voterId, true, _ct))
            .ReturnsAsync(new ReviewHelpfulMutationResult(IsHelpful: true, HelpfulCount: 1, Changed: true));

        var result = await _handler.Handle(
            new SetReviewHelpfulCommand(review.Id, true, voterId),
            _reviewRepoMock.Object, _cacheMock.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.IsHelpful.Should().BeTrue();
        result.Data.HelpfulCount.Should().Be(1);
        _cacheMock.Verify(c => c.RemoveAsync(It.Is<CacheKey>(k => k.Key.Contains(review.CoffeeShopId.ToString()))), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNothingChanged_DoesNotInvalidateCache()
    {
        var review = CreateReview(Guid.NewGuid());
        var voterId = Guid.NewGuid();
        _reviewRepoMock.Setup(r => r.GetById(review.Id, _ct)).ReturnsAsync(review);
        _reviewRepoMock.Setup(r => r.SetHelpfulAsync(review.Id, voterId, true, _ct))
            .ReturnsAsync(new ReviewHelpfulMutationResult(IsHelpful: true, HelpfulCount: 1, Changed: false));

        await _handler.Handle(
            new SetReviewHelpfulCommand(review.Id, true, voterId),
            _reviewRepoMock.Object, _cacheMock.Object, _ct);

        _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<CacheKey>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAuthorVotesOwnReview_ThrowsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var review = CreateReview(ownerId);
        _reviewRepoMock.Setup(r => r.GetById(review.Id, _ct)).ReturnsAsync(review);

        var act = async () => await _handler.Handle(
            new SetReviewHelpfulCommand(review.Id, true, ownerId),
            _reviewRepoMock.Object, _cacheMock.Object, _ct);

        await act.Should().ThrowAsync<ForbiddenException>();
        _reviewRepoMock.Verify(r => r.SetHelpfulAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenReviewMissing_ThrowsNotFound()
    {
        var reviewId = Guid.NewGuid();
        _reviewRepoMock.Setup(r => r.GetById(reviewId, _ct))
            .ReturnsAsync((Domain.Aggregates.ReviewAggregate.Review?)null);

        var act = async () => await _handler.Handle(
            new SetReviewHelpfulCommand(reviewId, true, Guid.NewGuid()),
            _reviewRepoMock.Object, _cacheMock.Object, _ct);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
