using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Beans;

public class CreateCoffeeBeanHandlerTests
{
    private readonly Mock<ICoffeeBeanRepository> repository = new();
    private readonly Mock<IMapper> mapper = new();

    [Fact]
    public async Task NewName_TracksBeanForCommit()
    {
        repository.Setup(r => r.GetByNameAsync("Arabica", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CoffeeBean?)null);
        mapper.Setup(m => m.Map<CoffeeBeansDto>(It.IsAny<CoffeeBean>()))
            .Returns((CoffeeBean bean) => new CoffeeBeansDto { Id = bean.Id, Name = bean.Name });

        var result = await CreateCoffeeBeanHandler.Handle(
            new CreateCoffeeBeanCommand("Arabica"), repository.Object, mapper.Object,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Arabica");
        repository.Verify(r => r.Add(It.Is<CoffeeBean>(bean => bean.Id == result.Data.Id)), Times.Once);
    }

    [Fact]
    public async Task DuplicateName_ReturnsConflict()
    {
        repository.Setup(r => r.GetByNameAsync("Arabica", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoffeeBean("Arabica"));

        var result = await CreateCoffeeBeanHandler.Handle(
            new CreateCoffeeBeanCommand("Arabica"), repository.Object, mapper.Object,
            TestContext.Current.CancellationToken);

        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        repository.Verify(r => r.Add(It.IsAny<CoffeeBean>()), Times.Never);
    }
}
