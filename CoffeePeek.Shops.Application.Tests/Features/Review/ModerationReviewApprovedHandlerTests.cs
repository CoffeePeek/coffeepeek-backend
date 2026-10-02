#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Infrastructure.Consumers;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Review;

using DomainReview = CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review;

public class ModerationReviewApprovedHandlerTests
{
    [Fact]
    public async Task Handle_AdminRemovedReviewIsNotRepublishedOrChanged()
    {
        var review = DomainReview.Create(Guid.NewGuid(), Guid.NewGuid(), "tester", null, "original comment body", 3, 3, 3, Guid.NewGuid());
        review.RemoveByAdmin();
        var incoming = BuildEvent(review.ModerationReviewId!.Value);
        _reviewRepoMock.Setup(x => x.GetByModerationReviewId(incoming.Review.Id, _ct)).ReturnsAsync(review);
        var result = await ModerationReviewApprovedHandler.Handle(incoming, _reviewRepoMock.Object, _unitOfWorkMock.Object, _cacheMock.Object, _ct);
        result.Should().BeNull();
        review.Comment.Should().Be("original comment body");
        review.IsSoftDelete.Should().BeTrue();
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(_ct), Times.Never);
    }

    private readonly Mock<IReviewRepository> _reviewRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static ModerationReviewApprovedEvent BuildEvent(Guid moderationReviewId, PhotoMetadataDto[]? photos = null)
    {
        var dto = new ModerationReviewDto
        {
            Id = moderationReviewId,
            ShopId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserName = "tester",
            Header = "new header",
            Comment = "new comment body text",
            Rating = new RatingDto { Place = 5, Service = 4, Coffee = 3 },
            DrinkSlug = "other",
            CustomDrinkName = "Эспрессо-тоник",
            DrinkNameRu = "Эспрессо-тоник",
            DrinkNameEn = "Эспрессо-тоник",
            Photos = photos ?? []
        };

        return new ModerationReviewApprovedEvent(dto);
    }

    // Moderation always sends SortIndex = 0; the handler must derive order from array position.
    private static PhotoMetadataDto Photo(string name, Guid ownerId, long size = 1234) => new()
    {
        Id = Guid.NewGuid(),
        FileName = $"{name}.jpg",
        ContentType = "image/jpeg",
        StorageKey = $"reviews/{name}.jpg",
        SizeBytes = size,
        OwnerId = ownerId,
        SortIndex = 0
    };

    [Fact]
    public async Task Handle_WhenNoLinkedReviewExists_CopiesPhotosInOrder()
    {
        var ownerId = Guid.NewGuid();
        PhotoMetadataDto[] photos = [Photo("p1", ownerId, 100), Photo("p2", ownerId, 200), Photo("p3", ownerId, 300)];
        var @event = BuildEvent(Guid.NewGuid(), photos);

        _reviewRepoMock.Setup(r => r.GetByModerationReviewId(@event.Review.Id, _ct))
            .ReturnsAsync((DomainReview?)null);

        DomainReview? added = null;
        _reviewRepoMock.Setup(r => r.Add(It.IsAny<DomainReview>()))
            .Callback<DomainReview>(r => added = r);

        await ModerationReviewApprovedHandler.Handle(
            @event, _reviewRepoMock.Object, _unitOfWorkMock.Object, _cacheMock.Object, _ct);

        added.Should().NotBeNull();
        added!.DrinkSlug.Should().Be("other");
        added.CustomDrinkName.Should().Be("Эспрессо-тоник");
        var published = added.Photos.ToList();
        published.Should().HaveCount(3);
        for (var i = 0; i < photos.Length; i++)
        {
            published[i].SortIndex.Should().Be(i);
            published[i].FileName.Should().Be(photos[i].FileName);
            published[i].ContentType.Should().Be(photos[i].ContentType);
            published[i].StorageKey.Should().Be(photos[i].StorageKey);
            published[i].SizeBytes.Should().Be(photos[i].SizeBytes);
            published[i].OwnerId.Should().Be(photos[i].OwnerId);
        }
    }

    [Fact]
    public async Task Handle_WhenLinkedReviewExists_ReplacesPhotos()
    {
        var ownerId = Guid.NewGuid();
        var @event = BuildEvent(Guid.NewGuid(), [Photo("n1", ownerId), Photo("n2", ownerId)]);
        var dto = @event.Review;

        var existing = DomainReview.Create(
            dto.ShopId, dto.UserId, "tester", "old header", "old comment body",
            1, 1, 1, moderationReviewId: dto.Id);
        var old = new ShopPhoto("old.jpg", "image/jpeg", "reviews/old.jpg", 1, ownerId);
        existing.ReplacePhotos([old]);

        _reviewRepoMock.Setup(r => r.GetByModerationReviewId(dto.Id, _ct)).ReturnsAsync(existing);

        IReadOnlyCollection<ShopPhoto>? removedArg = null;
        IReadOnlyCollection<ShopPhoto>? addedArg = null;
        _reviewRepoMock
            .Setup(r => r.TrackPhotoReplacement(It.IsAny<IReadOnlyCollection<ShopPhoto>>(), It.IsAny<IReadOnlyCollection<ShopPhoto>>()))
            .Callback<IReadOnlyCollection<ShopPhoto>, IReadOnlyCollection<ShopPhoto>>((r, a) =>
            {
                removedArg = r;
                addedArg = a;
            });

        await ModerationReviewApprovedHandler.Handle(
            @event, _reviewRepoMock.Object, _unitOfWorkMock.Object, _cacheMock.Object, _ct);

        existing.DrinkSlug.Should().Be("other");
        existing.DrinkNameRu.Should().Be("Эспрессо-тоник");
        existing.Photos.Select(p => p.StorageKey).Should().Equal("reviews/n1.jpg", "reviews/n2.jpg");
        _reviewRepoMock.Verify(
            r => r.TrackPhotoReplacement(It.IsAny<IReadOnlyCollection<ShopPhoto>>(), It.IsAny<IReadOnlyCollection<ShopPhoto>>()),
            Times.Once);
        removedArg.Should().NotBeNull();
        removedArg!.Should().ContainSingle().Which.Should().BeSameAs(old);
        addedArg.Should().NotBeNull();
        addedArg!.Select(p => p.StorageKey).Should().Equal("reviews/n1.jpg", "reviews/n2.jpg");
        _reviewRepoMock.Verify(r => r.Add(It.IsAny<DomainReview>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCheckInOriginatedReviewApproved_PublishesPhotos()
    {
        // Shape produced by Moderation's CheckInCreatedHandler -> SendReviewToModeration:
        // header may be null, photos owned by the check-in author.
        var userId = Guid.NewGuid();
        var dto = new ModerationReviewDto
        {
            Id = Guid.NewGuid(),
            ShopId = Guid.NewGuid(),
            UserId = userId,
            UserName = "checkin-user",
            Header = null,
            Comment = "check-in note long enough",
            Rating = new RatingDto { Place = 4, Service = 4, Coffee = 5 },
            Photos = [Photo("c1", userId), Photo("c2", userId)]
        };
        var @event = new ModerationReviewApprovedEvent(dto);

        _reviewRepoMock.Setup(r => r.GetByModerationReviewId(dto.Id, _ct))
            .ReturnsAsync((DomainReview?)null);

        DomainReview? added = null;
        _reviewRepoMock.Setup(r => r.Add(It.IsAny<DomainReview>()))
            .Callback<DomainReview>(r => added = r);

        await ModerationReviewApprovedHandler.Handle(
            @event, _reviewRepoMock.Object, _unitOfWorkMock.Object, _cacheMock.Object, _ct);

        added.Should().NotBeNull();
        added!.Header.Should().BeNull();
        added.Photos.Select(p => p.StorageKey).Should().Equal("reviews/c1.jpg", "reviews/c2.jpg");
        added.Photos.Select(p => p.SortIndex).Should().Equal(0, 1);
        added.Photos.Should().OnlyContain(p => p.OwnerId == userId);
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
        result.Should().BeNull(); // re-approval must not bump the author's review count again

        _reviewRepoMock.Verify(r => r.Add(It.IsAny<DomainReview>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cacheMock.Verify(
            c => c.RemoveAsync(CacheKey.Shop.Detail(dto.ShopId)),
            Times.Once);
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
        result!.ReviewId.Should().Be(added.Id);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }
}
