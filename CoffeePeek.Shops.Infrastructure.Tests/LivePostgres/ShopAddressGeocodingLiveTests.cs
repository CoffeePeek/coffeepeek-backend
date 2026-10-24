using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public class ShopAddressGeocodingLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase _fixture;

    public ShopAddressGeocodingLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false, "success")]
    [InlineData(true, "success")]
    [InlineData(false, "not_found")]
    [InlineData(true, "not_found")]
    [InlineData(false, "unavailable")]
    [InlineData(true, "unavailable")]
    public async Task ProfileUpdate_CommitsFreshCoordinatesOrKeepsOriginalProfile(bool owner, string outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Geocoding city " + Guid.NewGuid().ToString("N"));
        var ownerId = Guid.NewGuid();
        var shop = new CoffeeShop(null, "Original cafe", null, PriceRange.Cheap, Guid.NewGuid());
        shop.AssignOwner(ownerId);
        shop.SetLocation(city.Id, "Old address", 53.9m, 27.5m);
        await using (var seed = _fixture.Context())
        {
            seed.Cities.Add(city);
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(ct);
        }

        var geocoder = new Mock<IAddressGeocoder>(MockBehavior.Strict);
        var setup = geocoder.Setup(g => g.GeocodeAsync("Corrected address", It.IsAny<CancellationToken>()));
        if (outcome == "unavailable") setup.ThrowsAsync(new HttpRequestException("Yandex unavailable"));
        else setup.ReturnsAsync(outcome == "success" ? new GeocodingResult(53.95m, 27.65m) : null);
        using var host = BuildHost(geocoder.Object);
        await host.StartAsync(ct);
        try
        {
            var location = new ShopLocationPatch(city.Id, "Corrected address", 53.9m, 27.5m);
            var bus = host.Services.GetRequiredService<IMessageBus>();
            Func<Task<Response<AdminPublishedShopDto>>> update = owner
                ? () => bus.InvokeAsync<Response<AdminPublishedShopDto>>(new UpdateOwnerCoffeeShopCommand(
                    shop.Id, ownerId, "Edited cafe", "Edited description", null, null, null, null,
                    location, null, null) { ExpectedRevision = 0 }, ct)
                : () => bus.InvokeAsync<Response<AdminPublishedShopDto>>(new UpdateAdminCoffeeShopCommand(
                    shop.Id, "Edited cafe", "Edited description", CoffeePeek.Contract.Enums.PriceRange.Moderate,
                    null, location, null, null, null) { ExpectedRevision = 0 }, ct);

            if (outcome == "not_found") await Assert.ThrowsAsync<ValidationException>(update);
            else if (outcome == "unavailable") await Assert.ThrowsAsync<ServiceUnavailableException>(update);
            else
            {
                var response = await update();
                Assert.True(response.IsSuccess);
                Assert.Equal(53.95m, response.Data!.Location!.Latitude);
                Assert.Equal(27.65m, response.Data.Location.Longitude);
                Assert.Equal(1, response.Data.ProfileRevision);
            }

            // A new context observes committed data, independent of the handler's tracked aggregate.
            await using var verify = _fixture.Context();
            var saved = await verify.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
            Assert.Equal(outcome == "success" ? "Corrected address" : "Old address", saved.Location.Address);
            Assert.Equal(outcome == "success" ? 53.95m : 53.9m, saved.Location.Latitude);
            Assert.Equal(outcome == "success" ? 27.65m : 27.5m, saved.Location.Longitude);
            Assert.Equal(outcome == "success" ? "Edited cafe" : "Original cafe", saved.Name);
            Assert.Equal(outcome == "success" ? 1 : 0, saved.ProfileRevision);
            geocoder.Verify(g => g.GeocodeAsync("Corrected address", It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private IHost BuildHost(IAddressGeocoder geocoder) => Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddScoped(_ => _fixture.Context());
            services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
            services.AddScoped<IQueryCityRepository, QueryCityRepository>();
            services.AddScoped<IQueryEquipmentRepository, QueryEquipmentRepository>();
            services.AddScoped<IQueryCoffeeBeanRepository, QueryCoffeeBeanRepository>();
            services.AddScoped<IQueryRoasterRepository, QueryRoasterRepository>();
            services.AddScoped<IQueryBrewMethodRepository, QueryBrewMethodRepository>();
            services.AddSingleton(geocoder);
            services.AddSingleton(Mock.Of<IStrictCacheInvalidator>());
            services.AddSingleton<IOptions<MediaPublicUrlOptions>>(Options.Create(new MediaPublicUrlOptions
            {
                PublicEndpoint = "https://media.example.test", ShopBucketName = "shops"
            }));
        })
        .UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ShopAddressGeocodingLiveTests).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(UpdateAdminCoffeeShopHandler));
            options.Discovery.IncludeType(typeof(UpdateOwnerCoffeeShopHandler));
            options.Discovery.IncludeType(typeof(ShopProfileCacheInvalidationRequestedHandler));
            options.UseRuntimeCompilation();
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IAddressGeocoder));
            options.PersistMessagesWithPostgresql(_fixture.ConnectionString, "wolverine");
            options.Policies.UseDurableLocalQueues();
            options.Durability.Mode = DurabilityMode.Solo;
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
        })
        .Build();
}
