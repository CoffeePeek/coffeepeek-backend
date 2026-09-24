#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shops.Application.Features.CheckIn.CreateCheckIn;
using CoffeePeek.Shops.Application.ValidationStrategy.CheckIn;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CheckIn;

public class CheckInPhotoValidationTests
{
    private readonly Mock<IQueryCoffeeShopRepository> _shopRepoMock = new();
    private readonly Mock<IQueryCheckInRepository> _checkInRepoMock = new();

    public CheckInPhotoValidationTests()
    {
        _shopRepoMock.Setup(r => r.Exists(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _checkInRepoMock.Setup(r => r.ExistsSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _checkInRepoMock.Setup(r => r.CountSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    private CheckInValidationStrategy CreateStrategy() => new(_shopRepoMock.Object, _checkInRepoMock.Object);

    private static UploadedPhotoDto Photo(string key) => new("a.jpg", "image/jpeg", key, 10);

    private static CreateCheckInCommand Command(bool isPublic, params UploadedPhotoDto[] photos) =>
        new CreateCheckInCommand(
            CoffeeShopId: Guid.NewGuid(),
            IsPublic: isPublic,
            VisitedAt: DateTime.UtcNow.AddHours(-1),
            Note: "This is a sufficiently long note for review comment",
            Photos: photos.ToList(),
            Rating: new RatingDto { Place = 5, Coffee = 5, Service = 5 })
        { UserId = Guid.NewGuid(), UserName = "tester" };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TooManyPhotos_IsRejected(bool isPublic)
    {
        var photos = Enumerable.Range(0, BusinessConstants.MaxCheckInPhotos + 1)
            .Select(i => Photo($"reviews/{i}.jpg"))
            .ToArray();

        var result = await CreateStrategy().ValidateAsync(Command(isPublic, photos), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("A check-in can have at most 5 photos");
        _shopRepoMock.Verify(r => r.Exists(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateStorageKey_IsRejected()
    {
        var result = await CreateStrategy().ValidateAsync(
            Command(false, Photo("reviews/a.jpg"), Photo("reviews/a.jpg")), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Duplicate photos are not allowed");
    }

    [Fact]
    public async Task StorageKeysDifferingOnlyByCase_AreNotDuplicates()
    {
        var result = await CreateStrategy().ValidateAsync(
            Command(false, Photo("reviews/a.jpg"), Photo("reviews/A.jpg")), CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task EmptyStorageKey_IsRejected(string? key)
    {
        var result = await CreateStrategy().ValidateAsync(Command(true, Photo(key!)), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Photo storage key is required");
    }

    [Fact]
    public async Task OverlongStorageKey_IsRejected()
    {
        var key = new string('k', BusinessConstants.MaxCheckInPhotoStorageKeyLength + 1);

        var result = await CreateStrategy().ValidateAsync(Command(false, Photo(key)), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Photo storage key is required");
    }

    [Fact]
    public async Task MaxPhotosWithUniqueKeys_IsValid()
    {
        var photos = Enumerable.Range(0, BusinessConstants.MaxCheckInPhotos)
            .Select(i => Photo($"reviews/{i}.jpg"))
            .ToArray();

        var result = await CreateStrategy().ValidateAsync(Command(true, photos), CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }
}
