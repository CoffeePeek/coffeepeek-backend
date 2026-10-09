using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Equipments;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Equipments;

public class DeleteEquipmentHandlerTests
{
    private readonly Mock<IEquipmentRepository> _repo = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly EquipmentCategory _category = new();

    [Fact]
    public async Task Handle_ExistingEquipment_RemovesAndReturnsSuccess()
    {
        var equipment = new Equipment("Hario", "V60-02", _category);
        _repo.Setup(r => r.GetByIdAsync(equipment.Id, _ct)).ReturnsAsync(equipment);

        var result = await DeleteEquipmentHandler.Handle(
            new DeleteEquipmentCommand(equipment.Id),
            _repo.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.Remove(equipment), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingEquipment_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((Equipment)null);

        var result = await DeleteEquipmentHandler.Handle(
            new DeleteEquipmentCommand(Guid.NewGuid()),
            _repo.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
