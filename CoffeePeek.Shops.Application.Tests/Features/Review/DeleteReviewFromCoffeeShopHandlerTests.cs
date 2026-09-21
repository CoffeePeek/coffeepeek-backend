using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Review.DeleteReviewFromCoffeeShop;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Review.DeleteReviewFromCoffeeShop;

public class DeleteReviewFromCoffeeShopHandlerTests
{
    private readonly Mock<IReviewRepository> _reviewRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static Domain.Aggregates.ReviewAggregate.Review CreateReview(Guid? ownerId = null)
    {
        return Domain.Aggregates.ReviewAggregate.Review.Create(
            Guid.NewGuid(),
            ownerId ?? Guid.NewGuid(),
            "tester",
            "header text",
            "comment text body",
            5, 5, 5,
            Guid.NewGuid());
    }

    [Fact]
    public async Task Handle_WhenOwnerDeletesOwnReview_SoftDeletesAndSaves()
    {
        var ownerId = Guid.NewGuid();
        var review = CreateReview(ownerId);
        _reviewRepoMock.Setup(r => r.GetById(review.Id, _ct)).ReturnsAsync(review);

        var handler = new DeleteReviewFromCoffeeShopHandler();
        var result = await handler.Handle(
            new DeleteReviewFromCoffeeShopCommand(review.Id, ownerId),
            _reviewRepoMock.Object,
            _unitOfWorkMock.Object,
            _cacheMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        review.IsSoftDelete.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cacheMock.Verify(
            c => c.RemoveAsync(CacheKey.Shop.Detail(review.CoffeeShopId)),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNonOwnerAttemptsDelete_ThrowsForbiddenExceptionAndDoesNotSave()
    {
        var ownerId = Guid.NewGuid();
        var differentUserId = Guid.NewGuid();
        var review = CreateReview(ownerId);
        _reviewRepoMock.Setup(r => r.GetById(review.Id, _ct)).ReturnsAsync(review);

        var handler = new DeleteReviewFromCoffeeShopHandler();
        var act = async () => await handler.Handle(
            new DeleteReviewFromCoffeeShopCommand(review.Id, differentUserId),
            _reviewRepoMock.Object,
            _unitOfWorkMock.Object,
            _cacheMock.Object,
            _ct);

        await act.Should().ThrowAsync<ForbiddenException>();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        review.IsSoftDelete.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenReviewNotFound_ThrowsNotFoundException()
    {
        var reviewId = Guid.NewGuid();
        _reviewRepoMock.Setup(r => r.GetById(reviewId, _ct))
            .ReturnsAsync((Domain.Aggregates.ReviewAggregate.Review?)null);

        var handler = new DeleteReviewFromCoffeeShopHandler();
        var act = async () => await handler.Handle(
            new DeleteReviewFromCoffeeShopCommand(reviewId, Guid.NewGuid()),
            _reviewRepoMock.Object,
            _unitOfWorkMock.Object,
            _cacheMock.Object,
            _ct);

        await act.Should().ThrowAsync<NotFoundException>();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
