using CoffeePeek.Contract.Dtos;
using CoffeePeek.Moderation.Application.Features.Review.UpdateCoffeeShopReview;
using FluentAssertions;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Contract.Events.Moderation;
using MapsterMapper;
using Moq;
using Wolverine;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public sealed class ReviewPhotoUpdateTests
{
    [Fact]
    public void MissingNestedRating_FailsValidationWithoutThrowing()
    {
        var command = new UpdateCoffeeShopReviewCommand(Guid.NewGuid(), Guid.NewGuid(), null,
            "A sufficiently long review comment", null!);
        new ReviewUpdateValidationStrategy().Validate(command).IsValid.Should().BeFalse();
    }

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Mock<IModerationReviewRepository> _repo = new();
    private readonly Mock<IReviewPhotoLookup> _lookup = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IMessageBus> _bus = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly ModerationReview _review;
    private readonly PhotoMetadata _retained;

    public ReviewPhotoUpdateTests()
    {
        var user = Guid.NewGuid();
        _retained = PhotoMetadata.Create("real.jpg", "image/jpeg", "own-existing", 987, user, null);
        _review = ModerationReview.Create(user, Guid.NewGuid(), null, "Author", "Original header",
            "Original sufficiently long comment", 5, 5, 5, [_retained]);
        _review.Approve(Guid.NewGuid());
        _repo.Setup(r => r.GetById(_review.Id, _ct)).ReturnsAsync(_review);
        _mapper.Setup(m => m.Map<ModerationReviewDto>(_review)).Returns(() => new ModerationReviewDto
        {
            Id = _review.Id,
            Photos = _review.ReviewPhotos.Select(p => new PhotoMetadataDto { Id = p.Id, FileName = p.FileName, ContentType = p.ContentType, StorageKey = p.StorageKey, SizeBytes = p.SizeBytes }).ToArray()
        });
    }

    private UpdateCoffeeShopReviewCommand Command(List<UploadedPhotoDto>? photos = null) =>
        new(_review.UserId, _review.Id, "Changed header", "Changed sufficiently long comment",
            new RatingDto { Coffee = 4, Service = 4, Place = 4 }) { Photos = photos };

    private Task<(Response<UpdateCoffeeShopReviewResponse>, ModerationReviewEditedEvent?)> Handle(UpdateCoffeeShopReviewCommand cmd) =>
        UpdateCoffeeShopReviewHandler.Handle(cmd, new ReviewUpdateValidationStrategy(), _repo.Object,
            _mapper.Object, _uow.Object, Mock.Of<IDrinkCatalog>(), _lookup.Object, _bus.Object, _ct);

    private static UploadedPhotoDto Photo(string key) => new("untrusted.exe", "bogus", key, 0);

    [Fact]
    public async Task NullPhotos_RetainExactEntitiesAndDoNotResolveOrConfirm()
    {
        var (_, outgoing) = await Handle(Command());
        _review.ReviewPhotos.Should().ContainSingle().Which.Should().BeSameAs(_retained);
        outgoing!.Review.Photos.Should().ContainSingle().Which.SizeBytes.Should().Be(987);
        _lookup.VerifyNoOtherCalls();
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmptyPhotos_ClearSourceAndEventWithoutDeletingStorage()
    {
        var (_, outgoing) = await Handle(Command([]));
        _review.ReviewPhotos.Should().BeEmpty();
        outgoing!.Review.Photos.Should().BeEmpty();
        _lookup.VerifyNoOtherCalls();
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetainedPhotos_IgnoreClientMetadataAndReuseIdentity()
    {
        await Handle(Command([Photo(_retained.StorageKey)]));
        _review.ReviewPhotos.Should().ContainSingle().Which.Should().BeSameAs(_retained);
        _retained.FileName.Should().Be("real.jpg");
        _retained.SizeBytes.Should().Be(987);
        _lookup.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetainedLegacyPhoto_PreservesNullOwnerAndOldShopLinkExactly()
    {
        var oldShopId = Guid.NewGuid();
        typeof(PhotoMetadata).GetProperty(nameof(PhotoMetadata.OwnerId))!.SetValue(_retained, null);
        typeof(PhotoMetadata).GetProperty(nameof(PhotoMetadata.ModerationShopId))!.SetValue(_retained, oldShopId);
        await Handle(Command([Photo(_retained.StorageKey)]));
        _review.ReviewPhotos.Should().ContainSingle().Which.Should().BeSameAs(_retained);
        _retained.OwnerId.Should().BeNull();
        _retained.ModerationShopId.Should().Be(oldShopId);
        _retained.FileName.Should().Be("real.jpg");
        _retained.SizeBytes.Should().Be(987);
        _lookup.VerifyNoOtherCalls();
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task KeyFromAnotherSourceAggregate_CannotBeRetainedEvenForSameAuthor()
    {
        var otherPhoto = PhotoMetadata.Create("other.jpg", "image/jpeg", "already-on-other-review", 50, _review.UserId, null);
        var otherReview = ModerationReview.Create(_review.UserId, Guid.NewGuid(), null, "Author", null,
            "Another sufficiently long review comment", 5, 5, 5, [otherPhoto]);
        _lookup.Setup(l => l.ResolveAsync(_review.UserId,
                It.Is<IReadOnlyCollection<string>>(keys => keys.Count == 1 && keys.Single() == otherPhoto.StorageKey), _ct))
            .ReturnsAsync(new ReviewPhotoLookupResult(true, []));
        var act = () => Handle(Command([Photo(otherPhoto.StorageKey)]));
        await act.Should().ThrowAsync<ValidationException>();
        _lookup.VerifyAll();
        otherReview.ReviewPhotos.Should().ContainSingle().Which.Should().BeSameAs(otherPhoto);
        AssertUnchanged();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("expired")]
    [InlineData("already-used")]
    [InlineData("not-uploaded")]
    public async Task UnresolvedKey_RejectsBeforeAnyMutationSaveOrEvent(string key)
    {
        _lookup.Setup(l => l.ResolveAsync(_review.UserId, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(new ReviewPhotoLookupResult(true, []));
        var act = () => Handle(Command([Photo(key)]) with { ClearDrink = true });
        await act.Should().ThrowAsync<ValidationException>();
        AssertUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LookupFailure_Is400Or503AndLeavesSourceUntouched(bool transient)
    {
        _lookup.Setup(l => l.ResolveAsync(_review.UserId, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(transient ? ReviewPhotoLookupResult.Unreachable : ReviewPhotoLookupResult.Failed);
        var act = () => Handle(Command([Photo("new")]));
        if (transient) await act.Should().ThrowAsync<ServiceUnavailableException>();
        else await act.Should().ThrowAsync<ValidationException>();
        AssertUnchanged();
    }

    [Fact]
    public async Task ForeignAuthor_IsRejectedBeforeMediaLookup()
    {
        var (response, outgoing) = await Handle(Command([Photo("new")]) with { UserId = Guid.NewGuid() });
        response.StatusCode.Should().Be(403);
        outgoing.Should().BeNull();
        _lookup.VerifyNoOtherCalls();
        AssertUnchanged();
    }

    [Fact]
    public async Task ValidMixedReplacement_UsesTrustedMetadataAndEnlistsOnlyNewKeysBeforeSave()
    {
        _lookup.Setup(l => l.ResolveAsync(_review.UserId, It.Is<IReadOnlyCollection<string>>(k => k.SequenceEqual(new[] { "new" })), _ct))
            .ReturnsAsync(new ReviewPhotoLookupResult(true, [new("new", "trusted.png", "image/png", 345)]));
        var sequence = new List<string>();
        ReviewPhotosAttachedEvent? attachment = null;
        _bus.Setup(b => b.PublishAsync(It.IsAny<ReviewPhotosAttachedEvent>(), null))
            .Callback<ReviewPhotosAttachedEvent, DeliveryOptions?>((e, _) => { attachment = e; sequence.Add("event"); })
            .Returns(ValueTask.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(_ct)).Callback(() => sequence.Add("save")).ReturnsAsync(1);
        var (_, outgoing) = await Handle(Command([Photo("own-existing"), Photo("new")]));
        sequence.Should().Equal("event", "save");
        attachment!.UserId.Should().Be(_review.UserId);
        attachment.ReviewId.Should().Be(_review.Id);
        attachment.StorageKeys.Should().Equal("new");
        _review.ReviewPhotos.First().Should().BeSameAs(_retained);
        var added = _review.ReviewPhotos.Last();
        added.FileName.Should().Be("trusted.png");
        added.ContentType.Should().Be("image/png");
        added.SizeBytes.Should().Be(345);
        added.OwnerId.Should().Be(_review.UserId);
        added.ModerationShopId.Should().BeNull();
        _review.ModerationStatus.Should().Be(ModerationStatus.Pending);
        outgoing!.Review.Photos.Should().HaveCount(2);
        _lookup.VerifyAll();
        _repo.Verify(r => r.AddPhotos(It.Is<IEnumerable<PhotoMetadata>>(p => p.Count() == 1 && p.Single().StorageKey == "new")), Times.Once);
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("too-many")]
    public async Task InvalidPhotos_RejectBeforeRepositoryOrLookup(string kind)
    {
        var photos = kind switch
        {
            "blank" => new List<UploadedPhotoDto> { Photo(" ") },
            "duplicate" => [Photo("same"), Photo("same")],
            "null" => [null!],
            _ => Enumerable.Range(0, 21).Select(i => Photo(i.ToString())).ToList()
        };
        var act = () => Handle(Command(photos));
        await act.Should().ThrowAsync<ValidationException>();
        _repo.Verify(r => r.GetById(It.IsAny<Guid>(), _ct), Times.Never);
        _lookup.VerifyNoOtherCalls();
        AssertUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedOrDuplicateMediaKeys_FailClosed(bool duplicate)
    {
        var photo = new ResolvedUploadedPhotoDto(duplicate ? "new" : "other", "real.png", "image/png", 12);
        _lookup.Setup(l => l.ResolveAsync(_review.UserId, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(new ReviewPhotoLookupResult(true, duplicate ? [photo, photo] : [photo]));
        var act = () => Handle(Command([Photo("new")]));
        await act.Should().ThrowAsync<ValidationException>();
        AssertUnchanged();
    }

    private void AssertUnchanged()
    {
        _review.Header.Should().Be("Original header");
        _review.Comment.Should().Be("Original sufficiently long comment");
        _review.Rating.Coffee.Should().Be(5);
        _review.ModerationStatus.Should().Be(ModerationStatus.Approved);
        _review.ReviewPhotos.Should().ContainSingle().Which.Should().BeSameAs(_retained);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _bus.VerifyNoOtherCalls();
    }
}
