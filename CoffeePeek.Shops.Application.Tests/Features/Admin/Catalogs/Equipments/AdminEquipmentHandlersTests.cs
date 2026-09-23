using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Equipments;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Equipments;

public class CreateEquipmentHandlerTests
{
    private readonly Mock<IEquipmentRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly EquipmentCategory _category = new();

    [Fact]
    public async Task Handle_NewBrandAndModel_CreatesEquipmentAndInvalidatesCache()
    {
        _repo.Setup(r => r.GetCategoryByIdAsync((int)EquipmentCategoryEnum.Grinder, _ct)).ReturnsAsync(_category);
        _repo.Setup(r => r.GetByBrandAndModelAsync("Hario", "V60-02", _ct)).ReturnsAsync((Equipment)null);
        _mapper.Setup(m => m.Map<EquipmentDto>(It.IsAny<Equipment>()))
            .Returns((Equipment e) => new EquipmentDto { Id = e.Id, Name = e.Name, Brand = e.Brand, Model = e.ModelName });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await CreateEquipmentHandler.Handle(
            new CreateEquipmentCommand("Hario", "V60-02", EquipmentCategoryEnum.Grinder),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Brand.Should().Be("Hario");
        result.Data.Model.Should().Be("V60-02");
        _repo.Verify(r => r.Add(It.IsAny<Equipment>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.Equipment.ListPattern(), _ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateBrandAndModel_ReturnsConflict()
    {
        var existing = new Equipment("Hario", "V60-02", _category);
        _repo.Setup(r => r.GetCategoryByIdAsync((int)EquipmentCategoryEnum.Grinder, _ct)).ReturnsAsync(_category);
        _repo.Setup(r => r.GetByBrandAndModelAsync("Hario", "V60-02", _ct)).ReturnsAsync(existing);

        var result = await CreateEquipmentHandler.Handle(
            new CreateEquipmentCommand("Hario", "V60-02", EquipmentCategoryEnum.Grinder),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _repo.Verify(r => r.Add(It.IsAny<Equipment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidCategory_ReturnsBadRequest()
    {
        _repo.Setup(r => r.GetCategoryByIdAsync(It.IsAny<int>(), _ct)).ReturnsAsync((EquipmentCategory)null);

        var result = await CreateEquipmentHandler.Handle(
            new CreateEquipmentCommand("Hario", "V60-02", (EquipmentCategoryEnum)999),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        _repo.Verify(r => r.Add(It.IsAny<Equipment>()), Times.Never);
    }
}

public class UpdateEquipmentHandlerTests
{
    private readonly Mock<IEquipmentRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly EquipmentCategory _category = new();

    [Fact]
    public async Task Handle_ExistingEquipment_UpdatesFields()
    {
        var equipment = new Equipment("Sony", "Alpha A7 IV", _category);
        _repo.Setup(r => r.GetByIdAsync(equipment.Id, _ct)).ReturnsAsync(equipment);
        _repo.Setup(r => r.GetCategoryByIdAsync((int)EquipmentCategoryEnum.Grinder, _ct)).ReturnsAsync(_category);
        _mapper.Setup(m => m.Map<EquipmentDto>(It.IsAny<Equipment>()))
            .Returns((Equipment e) => new EquipmentDto { Id = e.Id, Name = e.Name, Brand = e.Brand, Model = e.ModelName });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await UpdateEquipmentHandler.Handle(
            new UpdateEquipmentCommand(equipment.Id, "Hario", "V60-02", EquipmentCategoryEnum.Grinder),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Brand.Should().Be("Hario");
        result.Data.Model.Should().Be("V60-02");
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingEquipment_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((Equipment)null);

        var result = await UpdateEquipmentHandler.Handle(
            new UpdateEquipmentCommand(Guid.NewGuid(), "Hario", "V60-02", EquipmentCategoryEnum.Grinder),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Handle_InvalidCategory_ReturnsBadRequest()
    {
        var equipment = new Equipment("Sony", "Alpha A7 IV", _category);
        _repo.Setup(r => r.GetByIdAsync(equipment.Id, _ct)).ReturnsAsync(equipment);
        _repo.Setup(r => r.GetCategoryByIdAsync(It.IsAny<int>(), _ct)).ReturnsAsync((EquipmentCategory)null);

        var result = await UpdateEquipmentHandler.Handle(
            new UpdateEquipmentCommand(equipment.Id, "Hario", "V60-02", (EquipmentCategoryEnum)999),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }
}

public class DeleteEquipmentHandlerTests
{
    private readonly Mock<IEquipmentRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly EquipmentCategory _category = new();

    [Fact]
    public async Task Handle_ExistingEquipment_RemovesAndReturnsSuccess()
    {
        var equipment = new Equipment("Hario", "V60-02", _category);
        _repo.Setup(r => r.GetByIdAsync(equipment.Id, _ct)).ReturnsAsync(equipment);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await DeleteEquipmentHandler.Handle(
            new DeleteEquipmentCommand(equipment.Id),
            _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.Remove(equipment), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingEquipment_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((Equipment)null);

        var result = await DeleteEquipmentHandler.Handle(
            new DeleteEquipmentCommand(Guid.NewGuid()),
            _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
