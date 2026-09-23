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

public class CreateBrewMethodHandlerTests
{
    private readonly Mock<IBrewMethodRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_NewName_CreatesBrewMethodAndInvalidatesCache()
    {
        _repo.Setup(r => r.GetByNameAsync("V60", _ct)).ReturnsAsync((DomainBrewMethod)null);
        _mapper.Setup(m => m.Map<BrewMethodDto>(It.IsAny<DomainBrewMethod>()))
            .Returns((DomainBrewMethod b) => new BrewMethodDto
            {
                Id = b.Id, Name = b.Name, Category = (BrewMethodCategoryEnum)(int)b.Category
            });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await CreateBrewMethodHandler.Handle(
            new CreateBrewMethodCommand("V60", BrewMethodCategoryEnum.Gravity),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("V60");
        result.Data.Category.Should().Be(BrewMethodCategoryEnum.Gravity);
        _repo.Verify(r => r.Add(It.IsAny<DomainBrewMethod>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.BrewMethod.ListPattern(), _ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict()
    {
        var existing = new DomainBrewMethod("V60", BrewMethodCategory.Gravity);
        _repo.Setup(r => r.GetByNameAsync("V60", _ct)).ReturnsAsync(existing);

        var result = await CreateBrewMethodHandler.Handle(
            new CreateBrewMethodCommand("V60", BrewMethodCategoryEnum.Gravity),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _repo.Verify(r => r.Add(It.IsAny<DomainBrewMethod>()), Times.Never);
    }
}

public class UpdateBrewMethodHandlerTests
{
    private readonly Mock<IBrewMethodRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ExistingBrewMethod_UpdatesNameAndCategory()
    {
        var brewMethod = new DomainBrewMethod("V60", BrewMethodCategory.Gravity);
        _repo.Setup(r => r.GetByIdAsync(brewMethod.Id, _ct)).ReturnsAsync(brewMethod);
        _mapper.Setup(m => m.Map<BrewMethodDto>(It.IsAny<DomainBrewMethod>()))
            .Returns((DomainBrewMethod b) => new BrewMethodDto
            {
                Id = b.Id, Name = b.Name, Category = (BrewMethodCategoryEnum)(int)b.Category
            });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await UpdateBrewMethodHandler.Handle(
            new UpdateBrewMethodCommand(brewMethod.Id, "Chemex", BrewMethodCategoryEnum.Gravity),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Chemex");
        result.Data.Category.Should().Be(BrewMethodCategoryEnum.Gravity);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingBrewMethod_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainBrewMethod)null);

        var result = await UpdateBrewMethodHandler.Handle(
            new UpdateBrewMethodCommand(Guid.NewGuid(), "Chemex", BrewMethodCategoryEnum.Gravity),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}

public class DeleteBrewMethodHandlerTests
{
    private readonly Mock<IBrewMethodRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ExistingBrewMethod_RemovesAndReturnsSuccess()
    {
        var brewMethod = new DomainBrewMethod("V60", BrewMethodCategory.Gravity);
        _repo.Setup(r => r.GetByIdAsync(brewMethod.Id, _ct)).ReturnsAsync(brewMethod);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await DeleteBrewMethodHandler.Handle(
            new DeleteBrewMethodCommand(brewMethod.Id),
            _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.Remove(brewMethod), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingBrewMethod_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainBrewMethod)null);

        var result = await DeleteBrewMethodHandler.Handle(
            new DeleteBrewMethodCommand(Guid.NewGuid()),
            _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
