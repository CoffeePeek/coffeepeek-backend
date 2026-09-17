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

public class ModerationReviewApprovedHandlerTests
{
    private readonly Mock<IReviewRepository> _reviewRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static ModerationReviewApprovedEvent BuildEvent(Guid moderationReviewId)
    {
        var dto = new ModerationReviewDto
        {
            Id = moderationReviewId,
            ShopId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserName = "tester",
            Header = "new header",
            Comment = "new comment body text",
            Rating = new RatingDto { Place = 5, Service = 4, Coffee = 3 }
        };

        return new ModerationReviewApprovedEvent(dto);
    }

    [Fact]
    public async Task Handle_WhenLinkedReviewExists_UpdatesAndRestoresInsteadOfSkipping()
    {
        // Regression guard for the silent-skip bug: a re-approval must update the SAME row,
        // not be dropped. Also proves the lookup is by ModerationReviewId (not AnyAsync).
        var @event = BuildEvent(Guid.NewGuid());
        var dto = @event.Review;

        // Simulate a previously-published review that was hidden by an edit.
        var existing = DomainReview.Create(
            dto.ShopId, dto.UserId, "tester", "old header", "old comment body",
            1, 1, 1, moderationReviewId: dto.Id);
        existing.Hide();

        _reviewRepoMock.Setup(r => r.GetByModerationReviewId(dto.Id, _ct)).ReturnsAsync(existing);

        var result = await ModerationReviewApprovedHandler.Handle(
            @event, _reviewRepoMock.Object, _unitOfWorkMock.Object, _cacheMock.Object, _ct);

        existing.Header.Should().Be("new header");
        existing.Comment.Should().Be("new comment body text");
        existing.Rating.Place.Should().Be(5);
        existing.Rating.Service.Should().Be(4);
        existing.Rating.Coffee.Should().Be(3);
        existing.IsSoftDelete.Should().BeFalse(); // restored / un-hidden
        result.ReviewId.Should().Be(existing.Id);

        _reviewRepoMock.Verify(r => r.Add(It.IsAny<DomainReview>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoLinkedReviewExists_CreatesLinkedReview()
    {
        var @event = BuildEvent(Guid.NewGuid());
        var dto = @event.Review;

        _reviewRepoMock.Setup(r => r.GetByModerationReviewId(dto.Id, _ct))
            .ReturnsAsync((DomainReview?)null);

        DomainReview? added = null;
        _reviewRepoMock.Setup(r => r.Add(It.IsAny<DomainReview>()))
            .Callback<DomainReview>(r => added = r);

        var result = await ModerationReviewApprovedHandler.Handle(
            @event, _reviewRepoMock.Object, _unitOfWorkMock.Object, _cacheMock.Object, _ct);

        _reviewRepoMock.Verify(r => r.Add(It.IsAny<DomainReview>()), Times.Once);
        added.Should().NotBeNull();
        added!.ModerationReviewId.Should().Be(dto.Id);
        result.ReviewId.Should().Be(added.Id);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }
}
