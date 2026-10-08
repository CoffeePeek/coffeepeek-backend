using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Cities;

public class DeleteCityHandlerTests
{
    private readonly Mock<ICityRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_UnusedCity_DeletesAndInvalidatesCache()
    {
        var cityId = Guid.NewGuid();
        _repo.Setup(r => r.DeleteIfUnusedAsync(cityId, _ct)).ReturnsAsync(CityDeleteResult.Deleted);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await DeleteCityHandler.Handle(new DeleteCityCommand(cityId), _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.DeleteIfUnusedAsync(cityId, _ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(It.IsAny<string>(), _ct), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_MissingCity_ReturnsNotFound()
    {
        _repo.Setup(r => r.DeleteIfUnusedAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync(CityDeleteResult.NotFound);
        var result = await DeleteCityHandler.Handle(new DeleteCityCommand(Guid.NewGuid()), _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        _cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ReferencedCity_ReturnsConflictWithoutInvalidation()
    {
        _repo.Setup(r => r.DeleteIfUnusedAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync(CityDeleteResult.Referenced);

        var result = await DeleteCityHandler.Handle(new DeleteCityCommand(Guid.NewGuid()), _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _cache.VerifyNoOtherCalls();
    }
}
