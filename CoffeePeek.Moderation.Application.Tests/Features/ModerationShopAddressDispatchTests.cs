using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Application.Features.Geocoding;
using CoffeePeek.Moderation.Application.Features.Shop.UpdateShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wolverine;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ModerationShopAddressDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModeratorChangesSubmittedAddress_WolverineDispatchesAndAppliesGeocoding(bool rejected)
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = ModerationShop.Create("Submitted cafe", Guid.NewGuid(), Guid.NewGuid(), null);
        shop.SetLocation(new ModerationLocation("Old address", 53.9m, 27.5m));
        if (rejected) shop.Reject("Correct the address");
        var shops = new Mock<IModerationShopRepository>();
        shops.Setup(r => r.GetByIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        shops.Setup(r => r.IsAwaitingGeocodingAsync(shop.Id, "Corrected address", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => shop.Location is { IsAddressValidated: false, Address: "Corrected address" });
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shops.Setup(r => r.TryApplyGeocodingAsync(shop.Id, "Corrected address", 54.1m, 28.2m, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                shop.Location!.SetLocation(54.1m, 28.2m, "Corrected address");
                applied.TrySetResult();
            })
            .ReturnsAsync(true);
        var geocoder = new Mock<IAddressGeocoder>(MockBehavior.Strict);
        geocoder.Setup(g => g.GeocodeAsync("Corrected address", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeocodingResult(54.1m, 28.2m));
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<ModerationShopDto>(It.IsAny<object>())).Returns(new ModerationShopDto());

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(shops.Object);
                services.AddSingleton(Mock.Of<IModerationRoasterRepository>());
                services.AddSingleton(geocoder.Object);
                services.AddSingleton(mapper.Object);
                services.AddSingleton(Mock.Of<IUnitOfWork>());
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(ModerationShopAddressDispatchTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(UpdateModerationCoffeeShopHandler));
                options.Discovery.IncludeType(typeof(ModerationGeocodingRequestedHandler));
                options.UseRuntimeCompilation();
            })
            .Build();
        await host.StartAsync(ct);
        try
        {
            var response = await host.Services.GetRequiredService<IMessageBus>()
                .InvokeAsync<UpdateEntityResponse<ModerationShopDto>>(
                    new UpdateModerationCoffeeShopCommand(new ModerationShopDto
                    {
                        Id = shop.Id, Name = shop.Name, CityId = shop.CityId, Address = "Corrected address"
                    }, Guid.NewGuid(), true), ct);

            Assert.True(response.IsSuccess);
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            Assert.True(shop.Location!.IsAddressValidated);
            Assert.Equal(54.1m, shop.Location.Latitude);
            Assert.Equal(28.2m, shop.Location.Longitude);
            geocoder.Verify(g => g.GeocodeAsync("Corrected address", It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
