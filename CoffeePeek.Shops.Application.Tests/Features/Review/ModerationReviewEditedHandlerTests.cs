using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Review;

using DomainReview = CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review;

public class ModerationReviewEditedHandlerTests
{
    private readonly Mock<IReviewRepository> _reviewRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static ModerationReviewEditedEvent BuildEvent(Guid moderationReviewId)
    {
        var dto = new ModerationReviewDto
        {
            Id = moderationReviewId,
            ShopId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserName = "tester",
            Header = "edited header",
            Comment = "edited comment body",
            Rating = new RatingDto { Place = 3, Service = 3, Coffee = 3 }
        };

        return new ModerationReviewEditedEvent(dto);
    }

    [Fact]
    public async Task Handle_WhenPublishedReviewLinked_HidesIt()
    {
        var @event = BuildEvent(Guid.NewGuid());
        var dto = @event.Review;

        var published = DomainReview.Create(
            dto.ShopId, dto.UserId, "tester", "header text", "comment body text",
            4, 4, 4, moderationReviewId: dto.Id);
        published.IsSoftDelete.Should().BeFalse();

        _reviewRepoMock.Setup(r => r.GetByModerationReviewId(dto.Id, _ct)).ReturnsAsync(published);

        await ModerationReviewEditedHandler.Handle(
            @event, _reviewRepoMock.Object, _unitOfWorkMock.Object, _cacheMock.Object, _ct);

        published.IsSoftDelete.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cacheMock.Verify(
            c => c.RemoveAsync(CacheKey.Shop.Detail(published.CoffeeShopId)),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoLinkedReview_IsNoOp()
    {
        var @event = BuildEvent(Guid.NewGuid());

        _reviewRepoMock.Setup(r => r.GetByModerationReviewId(@event.Review.Id, _ct))
            .ReturnsAsync((DomainReview?)null);

        await ModerationReviewEditedHandler.Handle(
            @event, _reviewRepoMock.Object, _unitOfWorkMock.Object, _cacheMock.Object, _ct);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
