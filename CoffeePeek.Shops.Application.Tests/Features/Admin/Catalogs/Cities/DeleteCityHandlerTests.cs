using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Cities;

public class DeleteCityHandlerTests
{
    private readonly Mock<ICityRepository> _repo = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_UnusedCity_DeletesAndReturnsSuccess()
    {
        var cityId = Guid.NewGuid();
        _repo.Setup(r => r.DeleteIfUnusedAsync(cityId, _ct)).ReturnsAsync(CityDeleteResult.Deleted);

        var result = await DeleteCityHandler.Handle(new DeleteCityCommand(cityId), _repo.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.DeleteIfUnusedAsync(cityId, _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingCity_ReturnsNotFound()
    {
        _repo.Setup(r => r.DeleteIfUnusedAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync(CityDeleteResult.NotFound);
        var result = await DeleteCityHandler.Handle(new DeleteCityCommand(Guid.NewGuid()), _repo.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Handle_ReferencedCity_ReturnsConflict()
    {
        _repo.Setup(r => r.DeleteIfUnusedAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync(CityDeleteResult.Referenced);

        var result = await DeleteCityHandler.Handle(new DeleteCityCommand(Guid.NewGuid()), _repo.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }
}
