using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Shops;

public class PublishedShopPhotosHandlerTests
{
    private readonly Mock<ICoffeeShopRepository> _shops = new Mock<ICoffeeShopRepository>();
    private readonly Mock<IUnitOfWork> _uow = new Mock<IUnitOfWork>();
    private readonly Mock<ICacheService> _cache = new Mock<ICacheService>();
    private readonly Mock<IUploadedPhotoLookup> _photos = new Mock<IUploadedPhotoLookup>();
    private readonly Mock<IMessageBus> _bus = new Mock<IMessageBus>();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly IOptions<MediaPublicUrlOptions> _media = Options.Create(new MediaPublicUrlOptions
    {
        PublicEndpoint = "https://cdn.example.com",
        ShopBucketName = "shops"
    });

    public PublishedShopPhotosHandlerTests()
    {
        _cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>())).Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);
        _uow.Setup(u => u.SaveChangesAsync(_ct)).ReturnsAsync(1);
        _photos.Setup(p => p.ClaimForShopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                ShopPhotoAttachmentKind.Gallery, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync((Guid _, Guid _, ShopPhotoAttachmentKind _, IReadOnlyCollection<string> keys, CancellationToken _) =>
                new ShopPhotoClaimResult(true, keys.Select(key =>
                    new ClaimedShopPhotoDto(Guid.NewGuid(), key, "verified.jpg", "image/jpeg", 512)).ToArray()));
    }

    [Fact]
    public async Task AddPhotos_AppendsToGallery()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        var actorId = Guid.NewGuid();
        _shops.Setup(r => r.TryAttachGalleryPhotosAsync(
                shop.Id, null, It.IsAny<IReadOnlyList<ShopPhoto>>(), _ct))
            .ReturnsAsync(true)
            .Callback<Guid, Guid?, IReadOnlyList<ShopPhoto>, CancellationToken>(
                (_, _, photos, _) => shop.AddPhotos(photos));
        _shops.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var result = await AddPublishedShopPhotosHandler.Handle(
            new AddPublishedShopPhotosCommand(shop.Id, actorId, null, [
                new UploadedPhotoDto("a.jpg", "image/jpeg", "key-a", 12)
            ]),
            _shops.Object, _uow.Object, _cache.Object, _media, _photos.Object, _bus.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        shop.ShopPhotos.Should().ContainSingle(p => p.StorageKey == "key-a" && p.OwnerId == actorId);
        shop.ShopPhotos.Single().FileName.Should().Be("verified.jpg");
        shop.ShopPhotos.Single().SizeBytes.Should().Be(512);
        result.Data!.Photos.Should().ContainSingle(p => p.StorageKey == "key-a" && p.SortIndex == 0);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.Is<ShopPhotosAttachedEvent>(e =>
            e.OwnerId == actorId && e.ShopId == shop.Id && e.Kind == ShopPhotoAttachmentKind.Gallery)), Times.Once);
    }

    [Fact]
    public async Task AddPhotos_EmptyList_ReturnsBadRequest()
    {
        var result = await AddPublishedShopPhotosHandler.Handle(
            new AddPublishedShopPhotosCommand(Guid.NewGuid(), Guid.NewGuid(), null, []),
            _shops.Object, _uow.Object, _cache.Object, _media, _photos.Object, _bus.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        _shops.Verify(
            r => r.TryAttachGalleryPhotosAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<ShopPhoto>>(), _ct),
            Times.Never);
    }

    [Fact]
    public async Task AddPhotos_UnknownShop_ReturnsNotFound()
    {
        var shopId = Guid.NewGuid();
        _shops.Setup(r => r.TryAttachGalleryPhotosAsync(
                shopId, null, It.IsAny<IReadOnlyList<ShopPhoto>>(), _ct))
            .ReturnsAsync(false);

        var result = await AddPublishedShopPhotosHandler.Handle(
            new AddPublishedShopPhotosCommand(shopId, Guid.NewGuid(), null, [
                new UploadedPhotoDto("a.jpg", "image/jpeg", "key-a", 12)
            ]),
            _shops.Object, _uow.Object, _cache.Object, _media, _photos.Object, _bus.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
    }

    [Fact]
    public async Task AddPhotos_ForeignOrMissingUpload_DoesNotWrite()
    {
        _photos.Setup(p => p.ClaimForShopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                ShopPhotoAttachmentKind.Gallery, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(new ShopPhotoClaimResult(true, []));
        var shopId = Guid.NewGuid();
        _shops.Setup(r => r.GetByIdAsync(shopId, _ct)).ReturnsAsync(
            new DomainCoffeeShop(shopId, "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid()));

        var act = () => AddPublishedShopPhotosHandler.Handle(
            new AddPublishedShopPhotosCommand(shopId, Guid.NewGuid(), null,
                [new UploadedPhotoDto("untrusted.jpg", "image/jpeg", "foreign-key", 1)]),
            _shops.Object, _uow.Object, _cache.Object, _media, _photos.Object, _bus.Object, _ct);

        await act.Should().ThrowAsync<ValidationException>();
        _shops.Verify(r => r.TryAttachGalleryPhotosAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(),
            It.IsAny<IReadOnlyList<ShopPhoto>>(), _ct), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
    }

    [Fact]
    public async Task AddPhotos_DuplicateKeys_DoesNotCallMediaOrWrite()
    {
        var shopId = Guid.NewGuid();
        _shops.Setup(r => r.GetByIdAsync(shopId, _ct)).ReturnsAsync(
            new DomainCoffeeShop(shopId, "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid()));
        var act = () => AddPublishedShopPhotosHandler.Handle(
            new AddPublishedShopPhotosCommand(shopId, Guid.NewGuid(), null,
                [new UploadedPhotoDto("a.jpg", "image/jpeg", "same-key", 1),
                    new UploadedPhotoDto("b.jpg", "image/jpeg", "same-key", 2)]),
            _shops.Object, _uow.Object, _cache.Object, _media, _photos.Object, _bus.Object, _ct);

        await act.Should().ThrowAsync<ValidationException>();
        _photos.VerifyNoOtherCalls();
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
    }

    [Fact]
    public async Task AddPhotos_MediaUnavailable_DoesNotWrite()
    {
        _photos.Setup(p => p.ClaimForShopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                ShopPhotoAttachmentKind.Gallery, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(ShopPhotoClaimResult.Unreachable);
        var shopId = Guid.NewGuid();
        _shops.Setup(r => r.GetByIdAsync(shopId, _ct)).ReturnsAsync(
            new DomainCoffeeShop(shopId, "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid()));
        var act = () => AddPublishedShopPhotosHandler.Handle(
            new AddPublishedShopPhotosCommand(shopId, Guid.NewGuid(), null,
                [new UploadedPhotoDto("a.jpg", "image/jpeg", "key-a", 1)]),
            _shops.Object, _uow.Object, _cache.Object, _media, _photos.Object, _bus.Object, _ct);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
    }

    [Fact]
    public async Task RemovePhotos_OwnerUnknownShop_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _shops.Setup(r => r.GetByIdForOwnerAsync(shopId, ownerId, _ct)).ReturnsAsync((DomainCoffeeShop?)null);

        var result = await RemovePublishedShopPhotosHandler.Handle(
            new RemovePublishedShopPhotosCommand(shopId, ownerId, [Guid.NewGuid()]),
            _shops.Object, _bus.Object, _media, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemovePhotos_RemovesPhotoAndPublishesAfterCommitInvalidation()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        var photo = new ShopPhoto("a.jpg", "image/jpeg", "key-a", 1, Guid.NewGuid());
        shop.AddPhotos([photo]);
        _shops.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var result = await RemovePublishedShopPhotosHandler.Handle(
            new RemovePublishedShopPhotosCommand(shop.Id, null, [photo.Id]),
            _shops.Object, _bus.Object, _media, _ct);

        result.IsSuccess.Should().BeTrue();
        shop.ShopPhotos.Should().BeEmpty();
        shop.ProfileRevision.Should().Be(1);
        _bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(e => e.ShopId == shop.Id)), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
        _cache.Verify(c => c.RemoveAsync(It.IsAny<CacheKey>()), Times.Never);
    }

    [Fact]
    public async Task RemovePhotos_EmptyList_IsNoOp()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        _shops.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var result = await RemovePublishedShopPhotosHandler.Handle(
            new RemovePublishedShopPhotosCommand(shop.Id, null, []),
            _shops.Object, _bus.Object, _media, _ct);

        result.IsSuccess.Should().BeTrue();
        shop.ProfileRevision.Should().Be(0);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ShopProfileCacheInvalidationRequested>()), Times.Never);
    }
}
