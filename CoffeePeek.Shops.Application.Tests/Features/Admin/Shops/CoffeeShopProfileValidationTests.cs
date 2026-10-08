using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Shops;

public class CoffeeShopProfileValidationTests
{
    private readonly Mock<ICoffeeShopRepository> _shops = new();
    private readonly Mock<IQueryCityRepository> _cities = new();
    private readonly Mock<IQueryEquipmentRepository> _equipment = new();
    private readonly Mock<IQueryCoffeeBeanRepository> _beans = new();
    private readonly Mock<IQueryRoasterRepository> _roasters = new();
    private readonly Mock<IQueryBrewMethodRepository> _brew = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IMessageBus> _bus = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly IOptions<MediaPublicUrlOptions> _media = Options.Create(new MediaPublicUrlOptions
    {
        PublicEndpoint = "https://cdn.example.com",
        ShopBucketName = "shops"
    });

    [Fact]
    public async Task Admin_InvalidName_RejectsWholePatchBeforeSchedulesChange()
    {
        var shop = NewShop();
        _shops.Setup(x => x.GetByIdWithCatalogsAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var action = () => UpdateAdminCoffeeShopHandler.Handle(
            new UpdateAdminCoffeeShopCommand(shop.Id, "   ", "Changed", ContractPriceRange.Expensive,
                DomainCoffeeShopStatus.TemporarilyClosed, null, null, [MondayClosed()], null),
            _shops.Object, _cities.Object, _equipment.Object, _beans.Object, _roasters.Object, _brew.Object,
            _bus.Object, _media, _ct);

        await action.Should().ThrowAsync<ValidationException>();
        shop.Name.Should().Be("Original");
        shop.Description.Should().BeNull();
        shop.PriceRange.Should().Be(DomainPriceRange.Cheap);
        shop.Status.Should().Be(DomainCoffeeShopStatus.Active);
        shop.Schedules.Should().BeEmpty();
        _unitOfWork.Verify(x => x.SaveChangesAsync(_ct), Times.Never);
    }

    [Theory]
    [InlineData(99, null)]
    [InlineData(1, 99)]
    public async Task Admin_InvalidEnum_RejectsWholePatch(int priceRange, int? status)
    {
        var shop = NewShop();
        _shops.Setup(x => x.GetByIdWithCatalogsAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var action = () => UpdateAdminCoffeeShopHandler.Handle(
            new UpdateAdminCoffeeShopCommand(shop.Id, "Changed", null, (ContractPriceRange)priceRange,
                status is null ? null : (DomainCoffeeShopStatus)status, null, null, [MondayClosed()], null),
            _shops.Object, _cities.Object, _equipment.Object, _beans.Object, _roasters.Object, _brew.Object,
            _bus.Object, _media, _ct);

        await action.Should().ThrowAsync<ValidationException>();
        shop.Name.Should().Be("Original");
        shop.Schedules.Should().BeEmpty();
        _unitOfWork.Verify(x => x.SaveChangesAsync(_ct), Times.Never);
    }

    [Fact]
    public async Task Owner_InvalidDescription_RejectsWholePatchBeforeSchedulesChange()
    {
        var shop = NewShop();
        var ownerId = Guid.NewGuid();
        _shops.Setup(x => x.GetByIdWithCatalogsForOwnerAsync(shop.Id, ownerId, _ct)).ReturnsAsync(shop);

        var action = () => UpdateOwnerCoffeeShopHandler.Handle(
            new UpdateOwnerCoffeeShopCommand(shop.Id, ownerId, "Changed", new string('x', 501),
                null, null, null, null, null, [MondayClosed()], null),
            _shops.Object, _cities.Object, _equipment.Object, _beans.Object, _roasters.Object, _brew.Object,
            _bus.Object, _media, _ct);

        await action.Should().ThrowAsync<ValidationException>();
        shop.Name.Should().Be("Original");
        shop.Schedules.Should().BeEmpty();
        _unitOfWork.Verify(x => x.SaveChangesAsync(_ct), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleProfileRevision_RejectsBeforeMutation(bool owner)
    {
        var shop = NewShop();
        var ownerId = Guid.NewGuid();
        _shops.Setup(x => x.GetByIdWithCatalogsAsync(shop.Id, _ct)).ReturnsAsync(shop);
        _shops.Setup(x => x.GetByIdWithCatalogsForOwnerAsync(shop.Id, ownerId, _ct)).ReturnsAsync(shop);

        Func<Task> action = owner
            ? () => UpdateOwnerCoffeeShopHandler.Handle(
                new UpdateOwnerCoffeeShopCommand(shop.Id, ownerId, "Changed", null,
                    null, null, null, null, null, [MondayClosed()], null)
                { ExpectedRevision = 1 },
                _shops.Object, _cities.Object, _equipment.Object, _beans.Object, _roasters.Object, _brew.Object,
                _bus.Object, _media, _ct)
            : () => UpdateAdminCoffeeShopHandler.Handle(
                new UpdateAdminCoffeeShopCommand(shop.Id, "Changed", null, ContractPriceRange.Expensive,
                    null, null, null, [MondayClosed()], null)
                { ExpectedRevision = 1 },
                _shops.Object, _cities.Object, _equipment.Object, _beans.Object, _roasters.Object, _brew.Object,
                _bus.Object, _media, _ct);

        await action.Should().ThrowAsync<ConflictException>();
        shop.Name.Should().Be("Original");
        shop.Schedules.Should().BeEmpty();
        _bus.Verify(b => b.PublishAsync(It.IsAny<ShopProfileCacheInvalidationRequested>()), Times.Never);
    }

    [Fact]
    public async Task Focus_InvalidEnum_ReturnsBadRequestWithoutMutation()
    {
        var shop = NewShop();
        shop.SetCoffeeFocus(CoffeeFocusType.Specialty);
        _shops.Setup(x => x.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var response = await SetAdminCoffeeShopFocusHandler.Handle(
            new SetAdminCoffeeShopFocusCommand(shop.Id, (CoffeeShopType)99, Guid.NewGuid()),
            _shops.Object, _unitOfWork.Object, _cache.Object, _media, _ct);

        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        shop.Type.Should().Be(CoffeeFocusType.Specialty);
        _unitOfWork.Verify(x => x.SaveChangesAsync(_ct), Times.Never);
    }

    private static DomainCoffeeShop NewShop() => new(null, "Original", null, DomainPriceRange.Cheap, Guid.NewGuid());

    private static ScheduleDto MondayClosed() => new(DayOfWeek.Monday, true, []);
}
