using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Shops;

public class UpdateAdminCoffeeShopHandlerTests
{
    private readonly Mock<ICoffeeShopRepository> _shops = new Mock<ICoffeeShopRepository>();
    private readonly Mock<IQueryCityRepository> _cities = new Mock<IQueryCityRepository>();
    private readonly Mock<IQueryEquipmentRepository> _equipment = new Mock<IQueryEquipmentRepository>();
    private readonly Mock<IQueryCoffeeBeanRepository> _beans = new Mock<IQueryCoffeeBeanRepository>();
    private readonly Mock<IQueryRoasterRepository> _roasters = new Mock<IQueryRoasterRepository>();
    private readonly Mock<IQueryBrewMethodRepository> _brew = new Mock<IQueryBrewMethodRepository>();
    private readonly Mock<IUnitOfWork> _uow = new Mock<IUnitOfWork>();
    private readonly Mock<ICacheService> _cache = new Mock<ICacheService>();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly IOptions<MediaPublicUrlOptions> _media = Options.Create(new MediaPublicUrlOptions
    {
        PublicEndpoint = "https://cdn.example.com",
        ShopBucketName = "shops"
    });

    public UpdateAdminCoffeeShopHandlerTests()
    {
        _cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>())).Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_UpdatesLocationContactsScheduleAndCatalogs()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Old", null, DomainPriceRange.Cheap, Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "Old address", 53.9m, 27.5m);
        var cityId = Guid.NewGuid();
        var equipment = new Equipment("La Marzocco", "Linea", new EquipmentCategory());

        _shops.Setup(r => r.GetByIdWithCatalogsAsync(shop.Id, _ct)).ReturnsAsync(shop);
        _cities.Setup(r => r.Exists(cityId, _ct)).ReturnsAsync(true);
        _equipment.Setup(r => r.GetByIds(It.IsAny<List<Guid>>(), _ct)).ReturnsAsync([equipment]);

        var result = await UpdateAdminCoffeeShopHandler.Handle(
            new UpdateAdminCoffeeShopCommand(
                shop.Id,
                "New name",
                "About",
                ContractPriceRange.Moderate,
                DomainCoffeeShopStatus.Active,
                new ShopLocationPatch(cityId, "Немига 5", 53.91m, 27.56m),
                new ShopContactsPatch("+375291112233", "a@b.c", "https://shop.by", "https://instagram.com/shop"),
                [
                    new ScheduleDto(DayOfWeek.Monday, false, [
                        new ShopScheduleIntervalDto { OpenTime = TimeSpan.FromHours(8), CloseTime = TimeSpan.FromHours(18) }
                    ])
                ],
                new ShopCatalogsPatch([equipment.Id], [], [], [])),
            _shops.Object, _cities.Object, _equipment.Object, _beans.Object, _roasters.Object, _brew.Object,
            _uow.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeTrue();
        shop.Name.Should().Be("New name");
        shop.Location.Address.Should().Be("Немига 5");
        shop.Location.CityId.Should().Be(cityId);
        shop.Contact.PhoneNumber.Should().Be("+375291112233");
        shop.Schedules.Should().ContainSingle(s => s.DayOfWeek == DayOfWeek.Monday && !s.IsClosed);
        shop.Equipments.Should().ContainSingle(e => e.Id == equipment.Id);
        result.Data!.Location!.Address.Should().Be("Немига 5");
        result.Data.Contacts!.PhoneNumber.Should().Be("+375291112233");
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(c => c.RemoveAsync(It.Is<CacheKey>(k => k.Key == CacheKey.Shop.Detail(shop.Id).Key)), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownCity_ReturnsBadRequest()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        var cityId = Guid.NewGuid();
        _shops.Setup(r => r.GetByIdWithCatalogsAsync(shop.Id, _ct)).ReturnsAsync(shop);
        _cities.Setup(r => r.Exists(cityId, _ct)).ReturnsAsync(false);

        var result = await UpdateAdminCoffeeShopHandler.Handle(
            new UpdateAdminCoffeeShopCommand(
                shop.Id, "Shop", null, ContractPriceRange.Cheap, null,
                new ShopLocationPatch(cityId, "Addr", 53m, 27m),
                null, null, null),
            _shops.Object, _cities.Object, _equipment.Object, _beans.Object, _roasters.Object, _brew.Object,
            _uow.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownEquipment_ReturnsBadRequest()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        _shops.Setup(r => r.GetByIdWithCatalogsAsync(shop.Id, _ct)).ReturnsAsync(shop);
        _equipment.Setup(r => r.GetByIds(It.IsAny<List<Guid>>(), _ct)).ReturnsAsync([]);

        var result = await UpdateAdminCoffeeShopHandler.Handle(
            new UpdateAdminCoffeeShopCommand(
                shop.Id, "Shop", null, ContractPriceRange.Cheap, null,
                null, null, null,
                new ShopCatalogsPatch([Guid.NewGuid()], null, null, null)),
            _shops.Object, _cities.Object, _equipment.Object, _beans.Object, _roasters.Object, _brew.Object,
            _uow.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        result.Message.Should().Contain("equipment");
    }

    [Fact]
    public async Task SetVisibility_InvalidatesSearchCache()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        _shops.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var result = await SetAdminCoffeeShopVisibilityHandler.Handle(
            new SetAdminCoffeeShopVisibilityCommand(shop.Id, Hidden: true),
            _shops.Object,
            _uow.Object,
            _cache.Object,
            _media,
            _ct);

        result.IsSuccess.Should().BeTrue();
        _cache.Verify(
            c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct),
            Times.Once);
    }

    [Theory]
    [InlineData(CoffeePeek.Contract.Enums.CoffeeShopType.Specialty, false)]
    [InlineData(CoffeePeek.Contract.Enums.CoffeeShopType.Cafe, true)]
    public async Task SetFocus_LeavesExplicitTagsUnchanged(CoffeePeek.Contract.Enums.CoffeeShopType type, bool hasSpecialtyTag)
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        shop.SetCoffeeFocus(hasSpecialtyTag ? CoffeeFocusType.Specialty : CoffeeFocusType.Cafe);
        var tagId = hasSpecialtyTag
            ? CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate.ShopTagIds.Specialty
            : Guid.NewGuid();
        shop.SetTags([tagId], Guid.NewGuid());
        var originalTag = System.Linq.Enumerable.Single(shop.ShopTags);
        _shops.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var result = await SetAdminCoffeeShopFocusHandler.Handle(
            new SetAdminCoffeeShopFocusCommand(shop.Id, type, Guid.NewGuid()),
            _shops.Object, _uow.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeTrue();
        shop.Type.Should().Be((CoffeeFocusType)(int)type);
        shop.ShopTags.Should().ContainSingle().Which.Should().BeSameAs(originalTag);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }
}

public class PublishedShopPhotosHandlerTests
{
    private readonly Mock<ICoffeeShopRepository> _shops = new Mock<ICoffeeShopRepository>();
    private readonly Mock<IUnitOfWork> _uow = new Mock<IUnitOfWork>();
    private readonly Mock<ICacheService> _cache = new Mock<ICacheService>();
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
            _shops.Object, _uow.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeTrue();
        shop.ShopPhotos.Should().ContainSingle(p => p.StorageKey == "key-a" && p.OwnerId == actorId);
        result.Data!.Photos.Should().ContainSingle(p => p.StorageKey == "key-a" && p.SortIndex == 0);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task AddPhotos_EmptyList_ReturnsBadRequest()
    {
        var result = await AddPublishedShopPhotosHandler.Handle(
            new AddPublishedShopPhotosCommand(Guid.NewGuid(), Guid.NewGuid(), null, []),
            _shops.Object, _uow.Object, _cache.Object, _media, _ct);

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
            _shops.Object, _uow.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
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
            _shops.Object, _uow.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
