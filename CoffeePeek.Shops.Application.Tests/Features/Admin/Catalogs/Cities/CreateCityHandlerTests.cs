using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Internal;
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
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_NewName_TracksCityForCommit()
    {
        _queryRepo.Setup(r => r.GetByName("Grodno", _ct)).ReturnsAsync((City)null!);
        _mapper.Setup(m => m.Map<CityDto>(It.IsAny<City>()))
            .Returns((City c) => new CityDto { Id = c.Id, Name = c.Name });

        var result = await CreateCityHandler.Handle(
            new CreateCityCommand("Grodno"),
            _queryRepo.Object, _repo.Object, _mapper.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Grodno");
        _repo.Verify(r => r.Add(It.IsAny<City>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict()
    {
        var existing = new City("Minsk");
        _queryRepo.Setup(r => r.GetByName("Minsk", _ct)).ReturnsAsync(existing);

        var result = await CreateCityHandler.Handle(
            new CreateCityCommand("Minsk"),
            _queryRepo.Object, _repo.Object, _mapper.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _repo.Verify(r => r.Add(It.IsAny<City>()), Times.Never);
    }
}
