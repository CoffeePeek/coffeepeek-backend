using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Roasters;

public class UpdateRoasterHandlerTests
{
    private readonly Mock<IRoasterRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IUploadedPhotoLookup> _photos = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ExistingRoaster_UpdatesNameAndReturnsSuccess()
    {
        var roaster = new Roaster("Coffee Circus");
        _repo.Setup(r => r.GetByIdAsync(roaster.Id, _ct)).ReturnsAsync(roaster);
        _mapper.Setup(m => m.Map<RoasterDto>(It.IsAny<Roaster>()))
            .Returns((Roaster r) => new RoasterDto { Id = r.Id, Name = r.Name });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await UpdateRoasterHandler.Handle(
            new UpdateRoasterCommand(roaster.Id, "Grunwald Coffee Roasters"),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _photos.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Grunwald Coffee Roasters");
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingRoaster_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((Roaster)null);

        var result = await UpdateRoasterHandler.Handle(
            new UpdateRoasterCommand(Guid.NewGuid(), "Grunwald Coffee Roasters"),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _photos.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
