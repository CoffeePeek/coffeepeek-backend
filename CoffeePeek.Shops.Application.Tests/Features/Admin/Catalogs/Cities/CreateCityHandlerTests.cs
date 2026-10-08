using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Cities;

public class CreateCityHandlerTests
{
    private readonly Mock<IQueryCityRepository> _queryRepo = new();
    private readonly Mock<ICityRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_NewName_CreatesCityAndInvalidatesCache()
    {
        _queryRepo.Setup(r => r.GetByName("Grodno", _ct)).ReturnsAsync((City)null!);
        _mapper.Setup(m => m.Map<CityDto>(It.IsAny<City>()))
            .Returns((City c) => new CityDto { Id = c.Id, Name = c.Name });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await CreateCityHandler.Handle(
            new CreateCityCommand("Grodno"),
            _queryRepo.Object, _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Grodno");
        _repo.Verify(r => r.Add(It.IsAny<City>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.City.ListPattern(), _ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict()
    {
        var existing = new City("Minsk");
        _queryRepo.Setup(r => r.GetByName("Minsk", _ct)).ReturnsAsync(existing);

        var result = await CreateCityHandler.Handle(
            new CreateCityCommand("Minsk"),
            _queryRepo.Object, _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _repo.Verify(r => r.Add(It.IsAny<City>()), Times.Never);
    }
}
