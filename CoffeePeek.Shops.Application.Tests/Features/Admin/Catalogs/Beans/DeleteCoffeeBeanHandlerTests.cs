using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Beans;

public class DeleteCoffeeBeanHandlerTests
{
    private readonly Mock<ICoffeeBeanRepository> repository = new();

    [Fact]
    public async Task ExistingBean_RemovesTrackedEntity()
    {
        var bean = new CoffeeBean("Arabica");
        repository.Setup(r => r.GetByIdAsync(bean.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bean);

        var result = await DeleteCoffeeBeanHandler.Handle(
            new DeleteCoffeeBeanCommand(bean.Id), repository.Object,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(r => r.Remove(bean), Times.Once);
    }

    [Fact]
    public async Task MissingBean_ReturnsNotFound()
    {
        repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CoffeeBean?)null);

        var result = await DeleteCoffeeBeanHandler.Handle(
            new DeleteCoffeeBeanCommand(Guid.NewGuid()), repository.Object,
            TestContext.Current.CancellationToken);

        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
