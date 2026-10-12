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

public class UpdateCoffeeBeanHandlerTests
{
    private readonly Mock<ICoffeeBeanRepository> repository = new();
    private readonly Mock<IMapper> mapper = new();

    [Fact]
    public async Task ExistingBean_UpdatesTrackedEntity()
    {
        var bean = new CoffeeBean("Arabica");
        repository.Setup(r => r.GetByIdAsync(bean.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bean);
        mapper.Setup(m => m.Map<CoffeeBeansDto>(It.IsAny<CoffeeBean>()))
            .Returns((CoffeeBean value) => new CoffeeBeansDto { Id = value.Id, Name = value.Name });

        var result = await UpdateCoffeeBeanHandler.Handle(
            new UpdateCoffeeBeanCommand(bean.Id, "Robusta"), repository.Object, mapper.Object,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Robusta");
        bean.Name.Should().Be("Robusta");
    }

    [Fact]
    public async Task MissingBean_ReturnsNotFound()
    {
        repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CoffeeBean?)null);

        var result = await UpdateCoffeeBeanHandler.Handle(
            new UpdateCoffeeBeanCommand(Guid.NewGuid(), "Robusta"), repository.Object, mapper.Object,
            TestContext.Current.CancellationToken);

        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
