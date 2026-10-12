using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;
using DomainBrewMethod = CoffeePeek.Shops.Domain.Aggregates.BrewMethods.BrewMethod;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.BrewMethods;

public class DeleteBrewMethodHandlerTests
{
    private readonly Mock<IBrewMethodRepository> _repo = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ExistingBrewMethod_RemovesAndReturnsSuccess()
    {
        var brewMethod = new DomainBrewMethod("V60", BrewMethodCategory.Gravity);
        _repo.Setup(r => r.GetByIdAsync(brewMethod.Id, _ct)).ReturnsAsync(brewMethod);

        var result = await DeleteBrewMethodHandler.Handle(
            new DeleteBrewMethodCommand(brewMethod.Id),
            _repo.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.Remove(brewMethod), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingBrewMethod_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainBrewMethod)null);

        var result = await DeleteBrewMethodHandler.Handle(
            new DeleteBrewMethodCommand(Guid.NewGuid()),
            _repo.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
