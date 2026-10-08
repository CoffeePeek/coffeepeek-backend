using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllCities;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Catalogs.GetAllCities;

public sealed class GetCitiesRequestHandlerTests
{
    [Fact]
    public async Task EveryRead_QueriesRepositoryAgainWithoutCache()
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Original city");
        var repository = new Mock<IQueryCityRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetAll(ct)).ReturnsAsync(() => [city]);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example.test",
            ShopBucketName = "shops"
        });
        var handler = new GetCitiesRequestHandler();

        var first = await handler.Handle(new GetCitiesCommand(), mapper, repository.Object, ct);
        city.Update("Updated city");
        var second = await handler.Handle(new GetCitiesCommand(), mapper, repository.Object, ct);

        Assert.Equal("Original city", Assert.Single(first.Data.Cities).Name);
        Assert.Equal("Updated city", Assert.Single(second.Data.Cities).Name);
        repository.Verify(r => r.GetAll(ct), Times.Exactly(2));
    }
}
