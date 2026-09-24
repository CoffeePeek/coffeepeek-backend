#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Validation;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shops.Application.Features.CheckIn.CreateCheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;
using Wolverine;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Tests.Features.CheckIn.CreateCheckIn;

public class CreateCheckInHandlerTests
{
    private readonly Mock<IQueryCheckInRepository> _checkInRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IMessageBus> _busMock = new();
    private readonly Mock<IAsyncValidationStrategy<CreateCheckInCommand>> _validationMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<IUploadedPhotoLookup> _photoLookupMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static CreateCheckInCommand BuildCommand(
        bool isPublic,
        RatingDto? rating = null,
        string note = "Some valid note here for testing purposes") =>
        new CreateCheckInCommand(
            CoffeeShopId: Guid.NewGuid(),
            IsPublic: isPublic,
            VisitedAt: DateTime.UtcNow.AddHours(-1),
            Note: note,
            Photos: null,
            Rating: rating ?? new RatingDto { Place = 5, Coffee = 5, Service = 5 },
            Header: "Explicit review title")
        { UserId = Guid.NewGuid(), UserName = "testuser" };

    private void SetupLookup(Guid userId, params ResolvedUploadedPhotoDto[] resolved) =>
        _photoLookupMock.Setup(l => l.ResolveAsync(userId, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(new UploadedPhotoLookupResult(true, resolved));

    private Task<CoffeePeek.Shared.Kernel.Response.Response<CreateCheckInResponse>> Run(CreateCheckInCommand command) =>
        CreateCheckInHandler.Handle(
            command,
            _checkInRepoMock.Object,
            _unitOfWorkMock.Object,
            _busMock.Object,
            _validationMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _photoLookupMock.Object,
            _ct);

    [Fact]
    public async Task Handle_PrivateCheckIn_CreatesCheckInAndSaves()
    {
        var command = BuildCommand(isPublic: false);
        _validationMock.Setup(v => v.ValidateAsync(command, _ct))
            .ReturnsAsync(ValidationResult.Valid);

        var result = await CreateCheckInHandler.Handle(
            command,
            _checkInRepoMock.Object,
            _unitOfWorkMock.Object,
            _busMock.Object,
            _validationMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _photoLookupMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        _busMock.Verify(b => b.PublishAsync(It.IsAny<object>()), Times.Never);
        _checkInRepoMock.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cacheMock.Verify(
            c => c.RemoveAsync(CacheKey.Shop.PublicPlatformStats()),
            Times.Once);
        _cacheMock.Verify(
            c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TakesPerUserLockBeforeValidating()
    {
        // Regression: cooldown/daily-cap checks raced; the lock must be held before they run.
        var command = BuildCommand(isPublic: false);
        var order = new List<string>();
        _checkInRepoMock.Setup(r => r.LockUserForCheckInAsync(command.UserId, _ct))
            .Callback(() => order.Add("lock")).Returns(Task.CompletedTask);
        _validationMock.Setup(v => v.ValidateAsync(command, _ct))
            .Callback(() => order.Add("validate")).ReturnsAsync(ValidationResult.Valid);

        await CreateCheckInHandler.Handle(command, _checkInRepoMock.Object, _unitOfWorkMock.Object, _busMock.Object,
            _validationMock.Object, _mapperMock.Object, _cacheMock.Object, _photoLookupMock.Object, _ct);

        order.Should().Equal("lock", "validate");
    }

    [Fact]
    public async Task Handle_WithPhotos_AttachesPhotosRegardlessOfVisibility()
    {
        var photos = new List<UploadedPhotoDto>
        {
            new("first.jpg", "image/jpeg", "checkins/first.jpg", 1024),
            new("second.jpg", "image/jpeg", "checkins/second.jpg", 2048)
        };
        var command = BuildCommand(isPublic: false) with { Photos = photos };
        _validationMock.Setup(v => v.ValidateAsync(command, _ct))
            .ReturnsAsync(ValidationResult.Valid);
        SetupLookup(command.UserId,
            new ResolvedUploadedPhotoDto("checkins/first.jpg", "first.jpg", "image/jpeg", 1024),
            new ResolvedUploadedPhotoDto("checkins/second.jpg", "second.jpg", "image/jpeg", 2048));

        DomainCheckIn? saved = null;
        _checkInRepoMock.Setup(r => r.Add(It.IsAny<DomainCheckIn>()))
            .Callback<DomainCheckIn>(c => saved = c);

        var result = await CreateCheckInHandler.Handle(
            command,
            _checkInRepoMock.Object,
            _unitOfWorkMock.Object,
            _busMock.Object,
            _validationMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _photoLookupMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.ShopPhotos.Should().HaveCount(2);
        saved.ShopPhotos.Select(p => p.StorageKey).Should()
            .BeEquivalentTo("checkins/first.jpg", "checkins/second.jpg");
    }

    [Fact]
    public async Task Handle_PublicCheckIn_WithValidRating_PublishesEventAndSaves()
    {
        var rating = new RatingDto { Place = 3, Service = 4, Coffee = 5 };
        var command = BuildCommand(
            isPublic: true,
            rating: rating,
            note: "This is a sufficiently long note for review comment");

        _validationMock.Setup(v => v.ValidateAsync(command, _ct))
            .ReturnsAsync(ValidationResult.Valid);
        _busMock.Setup(b => b.PublishAsync(It.IsAny<object>()))
            .Returns(ValueTask.CompletedTask);
        _mapperMock.Setup(m => m.Map<CoffeePeek.Contract.Dtos.CoffeeShop.ReviewDto>(It.IsAny<object>()))
            .Returns(new CoffeePeek.Contract.Dtos.CoffeeShop.ReviewDto());

        DomainCheckIn? saved = null;
        _checkInRepoMock.Setup(r => r.Add(It.IsAny<DomainCheckIn>()))
            .Callback<DomainCheckIn>(c => saved = c);

        var result = await CreateCheckInHandler.Handle(
            command,
            _checkInRepoMock.Object,
            _unitOfWorkMock.Object,
            _busMock.Object,
            _validationMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _photoLookupMock.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.Rating.Place.Should().Be(3);
        saved.Rating.Service.Should().Be(4);
        saved.Rating.Coffee.Should().Be(5);
        _busMock.Verify(b => b.PublishAsync(It.IsAny<object>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_PublicCheckIn_WithInvalidRating_ThrowsDomainException()
    {
        // Place=0 violates MinReviewRate=1 in Rating.Create — DomainException must propagate
        var rating = new RatingDto { Place = 0, Service = 3, Coffee = 3 };
        var note = "This note is long enough to serve as both a header and comment in the review";
        var command = BuildCommand(isPublic: true, rating: rating, note: note);

        // Application-level validation passes (mocked) but domain validation fails
        _validationMock.Setup(v => v.ValidateAsync(command, _ct))
            .ReturnsAsync(ValidationResult.Valid);

        Func<Task> act = async () => await CreateCheckInHandler.Handle(
            command,
            _checkInRepoMock.Object,
            _unitOfWorkMock.Object,
            _busMock.Object,
            _validationMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _photoLookupMock.Object,
            _ct);

        // TEST-04 regression: DomainException must NOT be swallowed by catch block
        await act.Should().ThrowAsync<DomainException>();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidCommand_ThrowsValidationException()
    {
        var command = BuildCommand(isPublic: false);
        _validationMock.Setup(v => v.ValidateAsync(command, _ct))
            .ReturnsAsync(ValidationResult.Invalid("CoffeeShopId is required"));

        Func<Task> act = async () => await CreateCheckInHandler.Handle(
            command,
            _checkInRepoMock.Object,
            _unitOfWorkMock.Object,
            _busMock.Object,
            _validationMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _photoLookupMock.Object,
            _ct);

        await act.Should().ThrowAsync<CoffeePeek.Shared.Kernel.Exceptions.ValidationException>();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnknownOrForeignPhoto_ThrowsAndPersistsNothing()
    {
        var command = BuildCommand(isPublic: false) with
        {
            Photos =
            [
                new UploadedPhotoDto("a.jpg", "image/jpeg", "reviews/mine", 1),
                new UploadedPhotoDto("b.jpg", "image/jpeg", "reviews/not-mine", 1)
            ]
        };
        _validationMock.Setup(v => v.ValidateAsync(command, _ct)).ReturnsAsync(ValidationResult.Valid);
        // Media omits keys that are unknown, foreign, already used or never uploaded.
        SetupLookup(command.UserId, new ResolvedUploadedPhotoDto("reviews/mine", "a.jpg", "image/jpeg", 10));

        var act = () => Run(command);

        (await act.Should().ThrowAsync<CoffeePeek.Shared.Kernel.Exceptions.ValidationException>())
            .WithMessage(CreateCheckInHandler.PhotosNotFoundMessage);
        _checkInRepoMock.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Never);
        _busMock.Verify(
            b => b.PublishAsync(It.IsAny<CheckInPhotosAttachedEvent>(), It.IsAny<DeliveryOptions?>()),
            Times.Never);
        _busMock.Verify(
            b => b.PublishAsync(It.IsAny<CheckinCreatedEvent>(), It.IsAny<DeliveryOptions?>()),
            Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMediaUnreachable_FailsClosed()
    {
        var command = BuildCommand(isPublic: false) with
        {
            Photos = [new UploadedPhotoDto("a.jpg", "image/jpeg", "reviews/mine", 1)]
        };
        _validationMock.Setup(v => v.ValidateAsync(command, _ct)).ReturnsAsync(ValidationResult.Valid);
        _photoLookupMock
            .Setup(l => l.ResolveAsync(command.UserId, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(UploadedPhotoLookupResult.Failed);

        var act = () => Run(command);

        (await act.Should().ThrowAsync<CoffeePeek.Shared.Kernel.Exceptions.ValidationException>())
            .WithMessage(CreateCheckInHandler.PhotosNotVerifiedMessage);
        _checkInRepoMock.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SpoofedClientMetadata_IsReplacedByResolvedMetadata()
    {
        var command = BuildCommand(isPublic: true, note: "This is a sufficiently long note for review comment") with
        {
            Photos = [new UploadedPhotoDto("evil.html", "text/html", "reviews/a.jpg", 1)]
        };
        _validationMock.Setup(v => v.ValidateAsync(command, _ct)).ReturnsAsync(ValidationResult.Valid);
        _mapperMock.Setup(m => m.Map<CoffeePeek.Contract.Dtos.CoffeeShop.ReviewDto>(It.IsAny<object>()))
            .Returns(new CoffeePeek.Contract.Dtos.CoffeeShop.ReviewDto());
        SetupLookup(command.UserId, new ResolvedUploadedPhotoDto("reviews/a.jpg", "a.jpg", "image/jpeg", 123456));

        DomainCheckIn? saved = null;
        _checkInRepoMock.Setup(r => r.Add(It.IsAny<DomainCheckIn>())).Callback<DomainCheckIn>(c => saved = c);
        CheckinCreatedEvent? created = null;
        _busMock.Setup(b => b.PublishAsync(It.IsAny<CheckinCreatedEvent>(), It.IsAny<DeliveryOptions?>()))
            .Callback<CheckinCreatedEvent, DeliveryOptions?>((e, _) => created = e)
            .Returns(ValueTask.CompletedTask);

        await Run(command);

        var photo = saved!.ShopPhotos.Should().ContainSingle().Subject;
        photo.FileName.Should().Be("a.jpg");
        photo.ContentType.Should().Be("image/jpeg");
        photo.SizeBytes.Should().Be(123456);

        created.Should().NotBeNull();
        created!.ReviewDto.Photos.Should().ContainSingle()
            .Which.Should().Be(new UploadedPhotoDto("a.jpg", "image/jpeg", "reviews/a.jpg", 123456));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WithPhotos_PublishesAttachEventForAnyVisibility(bool isPublic)
    {
        var command = BuildCommand(isPublic, note: "This is a sufficiently long note for review comment") with
        {
            Photos =
            [
                new UploadedPhotoDto("a.jpg", "image/jpeg", "reviews/a.jpg", 1),
                new UploadedPhotoDto("b.jpg", "image/jpeg", "reviews/b.jpg", 1)
            ]
        };
        _validationMock.Setup(v => v.ValidateAsync(command, _ct)).ReturnsAsync(ValidationResult.Valid);
        _mapperMock.Setup(m => m.Map<CoffeePeek.Contract.Dtos.CoffeeShop.ReviewDto>(It.IsAny<object>()))
            .Returns(new CoffeePeek.Contract.Dtos.CoffeeShop.ReviewDto());
        SetupLookup(command.UserId,
            new ResolvedUploadedPhotoDto("reviews/b.jpg", "b.jpg", "image/jpeg", 2),
            new ResolvedUploadedPhotoDto("reviews/a.jpg", "a.jpg", "image/jpeg", 1));

        DomainCheckIn? saved = null;
        _checkInRepoMock.Setup(r => r.Add(It.IsAny<DomainCheckIn>())).Callback<DomainCheckIn>(c => saved = c);
        CheckInPhotosAttachedEvent? attached = null;
        _busMock.Setup(b => b.PublishAsync(It.IsAny<CheckInPhotosAttachedEvent>(), It.IsAny<DeliveryOptions?>()))
            .Callback<CheckInPhotosAttachedEvent, DeliveryOptions?>((e, _) => attached = e)
            .Returns(ValueTask.CompletedTask);

        await Run(command);

        attached.Should().NotBeNull();
        attached!.UserId.Should().Be(command.UserId);
        attached.CheckInId.Should().Be(saved!.Id);
        attached.StorageKeys.Should().Equal("reviews/a.jpg", "reviews/b.jpg");
        // Client order is kept, not the lookup order.
        saved.ShopPhotos.Select(p => p.StorageKey).Should().Equal("reviews/a.jpg", "reviews/b.jpg");
    }

    [Fact]
    public async Task Handle_WithoutPhotos_NeverCallsLookupOrPublishesAttachEvent()
    {
        var command = BuildCommand(isPublic: false);
        _validationMock.Setup(v => v.ValidateAsync(command, _ct)).ReturnsAsync(ValidationResult.Valid);

        await Run(command);

        _photoLookupMock.VerifyNoOtherCalls();
        _busMock.Verify(
            b => b.PublishAsync(It.IsAny<CheckInPhotosAttachedEvent>(), It.IsAny<DeliveryOptions?>()),
            Times.Never);
    }
}
