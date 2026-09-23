using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Beans;

public class CreateCoffeeBeanHandlerTests
{
    private readonly Mock<ICoffeeBeanRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_NewName_CreatesBeanAndInvalidatesCache()
    {
        _repo.Setup(r => r.GetByNameAsync("Arabica", _ct)).ReturnsAsync((CoffeeBean)null);
        _mapper.Setup(m => m.Map<CoffeeBeansDto>(It.IsAny<CoffeeBean>()))
            .Returns((CoffeeBean b) => new CoffeeBeansDto { Id = b.Id, Name = b.Name });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await CreateCoffeeBeanHandler.Handle(
            new CreateCoffeeBeanCommand("Arabica"),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Arabica");
        _repo.Verify(r => r.Add(It.IsAny<CoffeeBean>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.CoffeeBean.ListPattern(), _ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict()
    {
        var existing = new CoffeeBean("Arabica");
        _repo.Setup(r => r.GetByNameAsync("Arabica", _ct)).ReturnsAsync(existing);

        var result = await CreateCoffeeBeanHandler.Handle(
            new CreateCoffeeBeanCommand("Arabica"),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _repo.Verify(r => r.Add(It.IsAny<CoffeeBean>()), Times.Never);
    }
}

public class UpdateCoffeeBeanHandlerTests
{
    private readonly Mock<ICoffeeBeanRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ExistingBean_UpdatesNameAndReturnsSuccess()
    {
        var bean = new CoffeeBean("Arabica");
        _repo.Setup(r => r.GetByIdAsync(bean.Id, _ct)).ReturnsAsync(bean);
        _mapper.Setup(m => m.Map<CoffeeBeansDto>(It.IsAny<CoffeeBean>()))
            .Returns((CoffeeBean b) => new CoffeeBeansDto { Id = b.Id, Name = b.Name });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await UpdateCoffeeBeanHandler.Handle(
            new UpdateCoffeeBeanCommand(bean.Id, "Robusta"),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Robusta");
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingBean_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((CoffeeBean)null);

        var result = await UpdateCoffeeBeanHandler.Handle(
            new UpdateCoffeeBeanCommand(Guid.NewGuid(), "Robusta"),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}

public class DeleteCoffeeBeanHandlerTests
{
    private readonly Mock<ICoffeeBeanRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ExistingBean_RemovesAndReturnsSuccess()
    {
        var bean = new CoffeeBean("Arabica");
        _repo.Setup(r => r.GetByIdAsync(bean.Id, _ct)).ReturnsAsync(bean);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await DeleteCoffeeBeanHandler.Handle(
            new DeleteCoffeeBeanCommand(bean.Id),
            _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.Remove(bean), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingBean_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((CoffeeBean)null);

        var result = await DeleteCoffeeBeanHandler.Handle(
            new DeleteCoffeeBeanCommand(Guid.NewGuid()),
            _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
