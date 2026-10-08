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

    [Fact]
    public async Task Handle_AssignedEquipmentCategoryChange_ReturnsConflictWithoutMutation()
    {
        var originalCategory = new EquipmentCategory();
        var nextCategory = new EquipmentCategory();
        typeof(EquipmentCategory).GetProperty(nameof(EquipmentCategory.Id))!
            .SetValue(originalCategory, 1);
        typeof(EquipmentCategory).GetProperty(nameof(EquipmentCategory.Id))!
            .SetValue(nextCategory, 2);
        var equipment = new Equipment("Original", "Model", originalCategory);
        _repo.Setup(r => r.GetByIdAsync(equipment.Id, _ct)).ReturnsAsync(equipment);
        _repo.Setup(r => r.GetCategoryByIdAsync(2, _ct)).ReturnsAsync(nextCategory);
        _repo.Setup(r => r.HasShopAssignmentsAsync(equipment.Id, _ct)).ReturnsAsync(true);

        var result = await UpdateEquipmentHandler.Handle(
            new UpdateEquipmentCommand(equipment.Id, "Changed", "Model", (EquipmentCategoryEnum)2),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        equipment.CategoryId.Should().Be(1);
        equipment.Brand.Should().Be("Original");
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
    }
}
