using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Roasters;

public class DeleteRoasterHandlerTests
{
    private readonly Mock<IRoasterRepository> _repo = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ExistingRoaster_RemovesAndReturnsSuccess()
    {
        var roaster = new Roaster("Coffee Circus");
        _repo.Setup(r => r.GetByIdAsync(roaster.Id, _ct)).ReturnsAsync(roaster);

        var result = await DeleteRoasterHandler.Handle(
            new DeleteRoasterCommand(roaster.Id),
            _repo.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.Remove(roaster), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingRoaster_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((Roaster)null);

        var result = await DeleteRoasterHandler.Handle(
            new DeleteRoasterCommand(Guid.NewGuid()),
            _repo.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
