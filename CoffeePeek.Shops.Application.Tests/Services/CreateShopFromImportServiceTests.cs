using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Constants;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Services;

public class CreateShopFromImportServiceTests
{
    private readonly Mock<IQueryCoffeeShopRepository> _shopRepo = new();
    private readonly Mock<IQueryShopTagRepository> _tagRepo = new();
    private readonly Mock<IQueryCityRepository> _cityRepo = new();
    private readonly Mock<IApplyShopMenuService> _applyMenu = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ILogger<CreateShopFromImportService>> _logger = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private CreateShopFromImportService CreateSut() =>
        new(_shopRepo.Object, _tagRepo.Object, _cityRepo.Object, _applyMenu.Object, _uow.Object, _cache.Object, _logger.Object);

    public CreateShopFromImportServiceTests()
    {
        _cityRepo.Setup(r => r.Exists(It.IsAny<Guid>(), _ct)).ReturnsAsync(true);
        _applyMenu
            .Setup(a => a.ApplySnapshotAsync(
                It.IsAny<Guid>(),
                It.IsAny<ShopMenuSnapshot>(),
                It.IsAny<bool>(),
                It.IsAny<Guid?>(),
                _ct))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Create_WhenNew_AddsShopWithFocusAndSpecialtyTag()
    {
        _shopRepo.Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct)).ReturnsAsync((Guid?)null);
        _tagRepo
            .Setup(r => r.GetActiveBySlugsAsync(It.Is<IReadOnlyCollection<string>>(s => s.Contains("specialty")), _ct))
            .ReturnsAsync([ShopTag.CreateWithId(ShopTagIds.Specialty, "specialty", "Specialty", null, 2)]);

        CoffeeShop? added = null;
        _shopRepo.Setup(r => r.Add(It.IsAny<CoffeeShop>())).Callback<CoffeeShop>(s => added = s);

        var candidateId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var item = new ImportCandidatePublishedItem(
            candidateId,
            creatorId,
            "Coffe Joy",
            "Немига 5",
            53.9152m,
            27.5847m,
            CitiesConsts.MinskId,
            null,
            null,
            null,
            Contract.Enums.CoffeeShopType.Specialty,
            ["to_go"],
            TemporarilyClosed: false);

        var shopId = await CreateSut().CreateShopFromImportAsync(item, _ct);

        shopId.Should().NotBe(Guid.Empty);
        added.Should().NotBeNull();
        added!.Type.Should().Be(CoffeeFocusType.Specialty);
        added.ShopTags.Select(t => t.TagId).Should().Contain(ShopTagIds.Specialty);
        added.ModerationId.Should().Be(candidateId);
        added.OwnerUserId.Should().BeNull();
        added.ImportedFromFileAt.Should().BeNull();
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Create_WhenImportedFromFile_MarksShop()
    {
        _shopRepo.Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct)).ReturnsAsync((Guid?)null);
        _tagRepo
            .Setup(r => r.GetActiveBySlugsAsync(It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync([]);

        CoffeeShop? added = null;
        _shopRepo.Setup(r => r.Add(It.IsAny<CoffeeShop>())).Callback<CoffeeShop>(s => added = s);

        var item = new ImportCandidatePublishedItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Surf Coffee",
            "Немига 5",
            53.9152m,
            27.5847m,
            CitiesConsts.MinskId,
            null,
            null,
            null,
            Contract.Enums.CoffeeShopType.Cafe,
            [],
            TemporarilyClosed: false,
            ImportedFromFile: true);

        await CreateSut().CreateShopFromImportAsync(item, _ct);

        added.Should().NotBeNull();
        added!.ImportedFromFileAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_WhenAlreadyExists_ReturnsExistingId()
    {
        var existing = Guid.NewGuid();
        _shopRepo.Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct)).ReturnsAsync(existing);

        var item = new ImportCandidatePublishedItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Coffe Joy",
            "Немига 5",
            53.9152m,
            27.5847m,
            CitiesConsts.MinskId,
            null,
            null,
            null,
            Contract.Enums.CoffeeShopType.Cafe,
            [],
            false);

        var shopId = await CreateSut().CreateShopFromImportAsync(item, _ct);

        shopId.Should().Be(existing);
        _shopRepo.Verify(r => r.Add(It.IsAny<CoffeeShop>()), Times.Never);
    }

    [Fact]
    public async Task Create_WithOsmMultiPhone_KeepsFirstNumber()
    {
        _shopRepo.Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct)).ReturnsAsync((Guid?)null);
        _tagRepo
            .Setup(r => r.GetActiveBySlugsAsync(It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync([]);

        CoffeeShop? added = null;
        _shopRepo.Setup(r => r.Add(It.IsAny<CoffeeShop>())).Callback<CoffeeShop>(s => added = s);

        var item = new ImportCandidatePublishedItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Coffe Joy",
            "Немига 5",
            53.9152m,
            27.5847m,
            CitiesConsts.MinskId,
            "+375 29 111-22-33; +375 17 200-00-00",
            null,
            null,
            Contract.Enums.CoffeeShopType.Cafe,
            [],
            false);

        await CreateSut().CreateShopFromImportAsync(item, _ct);

        added.Should().NotBeNull();
        added!.Contact.PhoneNumber.Should().Be("+375 29 111-22-33");
    }

    [Fact]
    public async Task Create_WhenConstCityIdMissing_UsesCityRowByName()
    {
        var minsk = new City("Минск");
        _cityRepo.Setup(r => r.Exists(CitiesConsts.MinskId, _ct)).ReturnsAsync(false);
        _cityRepo.Setup(r => r.GetByName("Минск", _ct)).ReturnsAsync(minsk);
        _shopRepo.Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct)).ReturnsAsync((Guid?)null);
        _tagRepo.Setup(r => r.GetActiveBySlugsAsync(It.IsAny<IReadOnlyCollection<string>>(), _ct)).ReturnsAsync([]);

        CoffeeShop? added = null;
        _shopRepo.Setup(r => r.Add(It.IsAny<CoffeeShop>())).Callback<CoffeeShop>(s => added = s);

        var item = new ImportCandidatePublishedItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Coffe Joy",
            "Немига 5",
            53.9152m,
            27.5847m,
            CitiesConsts.MinskId,
            null,
            null,
            null,
            Contract.Enums.CoffeeShopType.Cafe,
            [],
            false);

        await CreateSut().CreateShopFromImportAsync(item, _ct);

        added.Should().NotBeNull();
        added!.Location.CityId.Should().Be(minsk.Id);
        added.Location.CityId.Should().NotBe(CitiesConsts.MinskId);
    }

    [Fact]
    public async Task Create_WhenCityCannotBeResolved_ThrowsDomainException()
    {
        _cityRepo.Setup(r => r.Exists(It.IsAny<Guid>(), _ct)).ReturnsAsync(false);
        _cityRepo.Setup(r => r.GetByName(It.IsAny<string>(), _ct)).ReturnsAsync((City?)null);
        _shopRepo.Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct)).ReturnsAsync((Guid?)null);

        var item = new ImportCandidatePublishedItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Coffe Joy",
            "Немига 5",
            53.9152m,
            27.5847m,
            CitiesConsts.MinskId,
            null,
            null,
            null,
            Contract.Enums.CoffeeShopType.Cafe,
            [],
            false);

        var act = () => CreateSut().CreateShopFromImportAsync(item, _ct);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Минск*");
        _shopRepo.Verify(r => r.Add(It.IsAny<CoffeeShop>()), Times.Never);
    }

    [Fact]
    public async Task Create_WhenMenuSnapshotPresent_CopiesMenuAndUsesSuggestedPriceRange()
    {
        _shopRepo.Setup(r => r.GetIdByModerationId(It.IsAny<Guid>(), _ct)).ReturnsAsync((Guid?)null);
        _tagRepo
            .Setup(r => r.GetActiveBySlugsAsync(It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync([]);

        CoffeeShop? added = null;
        _shopRepo.Setup(r => r.Add(It.IsAny<CoffeeShop>())).Callback<CoffeeShop>(s => added = s);

        var menu = new ShopMenuSnapshot(
            DateTime.UtcNow,
            "BYN",
            Contract.Enums.PriceRange.Cheap,
            [new ShopMenuItemSnapshot("cappuccino", MenuItemAvailability.Present, 6.5m, null, MenuItemSource.Parsed)],
            [new ShopMenuPhotoSnapshot("menu.jpg", "image/jpeg", "menus/a.jpg", 10)]);

        var item = new ImportCandidatePublishedItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Coffe Joy",
            "Немига 5",
            53.9152m,
            27.5847m,
            CitiesConsts.MinskId,
            null,
            null,
            null,
            Contract.Enums.CoffeeShopType.Cafe,
            [],
            false,
            false,
            menu);

        await CreateSut().CreateShopFromImportAsync(item, _ct);

        added.Should().NotBeNull();
        added!.PriceRange.Should().Be(Domain.Aggregates.CoffeeShopAggregate.PriceRange.Cheap);
        _applyMenu.Verify(a => a.ApplySnapshotAsync(
            added.Id,
            menu,
            false,
            item.CreatorId,
            _ct), Times.Once);
    }
}
