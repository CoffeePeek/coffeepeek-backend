using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllCities;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CityCatalogFreshReadLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public CityCatalogFreshReadLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task NewReadContext_SeesCommittedCityUpdate()
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Original city " + Guid.NewGuid().ToString("N"));
        await using (var seed = fixture.Context())
        {
            seed.Cities.Add(city);
            await seed.SaveChangesAsync(ct);
        }

        var handler = new GetCitiesRequestHandler();
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example.test",
            ShopBucketName = "shops"
        });
        await using (var firstContext = fixture.Context())
        {
            var first = await handler.Handle(new GetCitiesCommand(), mapper,
                new QueryCityRepository(firstContext), ct);
            Assert.Equal(city.Name, Assert.Single(first.Data.Cities, x => x.Id == city.Id).Name);
        }

        const string updatedName = "Updated city";
        await using (var updateContext = fixture.Context())
        {
            var tracked = await updateContext.Cities.SingleAsync(x => x.Id == city.Id, ct);
            tracked.Update(updatedName);
            await updateContext.SaveChangesAsync(ct);
        }

        await using var secondContext = fixture.Context();
        var second = await handler.Handle(new GetCitiesCommand(), mapper,
            new QueryCityRepository(secondContext), ct);
        Assert.Equal(updatedName, Assert.Single(second.Data.Cities, x => x.Id == city.Id).Name);
    }
}
