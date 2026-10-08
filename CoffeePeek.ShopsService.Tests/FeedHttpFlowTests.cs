using System.Net;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shared.Web.Handlers;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.ShopsService.Controllers;
using JasperFx.CodeGeneration;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class FeedHttpFlowTests
{
    [Fact]
    public async Task HttpRequestDispatchesActualHandlerAndReturnsPublicCardOrValidationError()
    {
        var ct = TestContext.Current.CancellationToken;
        var shopId = Guid.NewGuid(); var authorId = Guid.NewGuid(); var checkInId = Guid.NewGuid();
        var published = DateTime.UtcNow.AddDays(-1);
        var reader = new Mock<ICheckInFeedReader>();
        reader.Setup(x => x.ReadAsync(new CheckInFeedScope(), 20, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckInFeedReadPage([new(new UnifiedCheckInDto
            {
                Id = checkInId, ShopId = shopId, UserId = authorId, Text = "A public visit",
                Visibility = CheckInVisibility.Public, ModerationState = CheckInModerationState.Approved
            }, published)], false));
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(x => x.GetForReadAsync(PublicAddressKind.CoffeeShop, It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PublicAddressMetadata(shopId, "example-shop", "/coffee-shops/example-shop", 1)]);
        var profiles = new Mock<IProfileAddressLookup>();
        profiles.Setup(x => x.GetByIdsAsync(It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PublicAddressMetadata(authorId, "example-author", "/users/example-author", 1)]);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Services.AddControllersModule();
        builder.Services.AddControllers().AddApplicationPart(typeof(FeedController).Assembly);
        builder.Services.AddAuthentication(); builder.Services.AddAuthorization();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(Mock.Of<IUserContext>());
        builder.Services.AddSingleton(reader.Object); builder.Services.AddSingleton(addresses.Object);
        builder.Services.AddSingleton(profiles.Object);
        builder.Services.AddScoped<IPublicCheckInProjector, PublicCheckInProjector>();
        builder.Host.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(FeedController).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(GetCheckInFeedHandler));
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
            foreach (var port in new[] { typeof(ICheckInFeedReader), typeof(IPublicCheckInProjector),
                         typeof(IPublicAddressService), typeof(IProfileAddressLookup) })
                options.CodeGeneration.AlwaysUseServiceLocationFor(port);
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UseExceptionHandler(); app.MapControllers();
        await app.StartAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var response = await client.GetAsync("/api/v1/feed", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var json = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(json);
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(JsonValueKind.Null, data.GetProperty("nextCursor").ValueKind);
            var item = Assert.Single(data.GetProperty("items").EnumerateArray());
            Assert.Equal(published, item.GetProperty("publishedAtUtc").GetDateTime());
            var card = item.GetProperty("checkIn");
            Assert.Equal(checkInId, card.GetProperty("id").GetGuid());
            Assert.Equal("Approved", card.GetProperty("moderationState").GetString());
            Assert.Equal("example-shop", card.GetProperty("shop").GetProperty("slug").GetString());
            Assert.DoesNotContain(shopId.ToString(), json); Assert.DoesNotContain(authorId.ToString(), json);
            using var invalid = await client.GetAsync("/api/v1/feed?pageSize=0", ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var unknown = await client.GetAsync("/api/v1/feed?citySlug=unknown", ct);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            reader.Verify(x => x.ReadAsync(It.IsAny<CheckInFeedScope>(), It.IsAny<int>(), It.IsAny<CheckInFeedPosition?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await app.StopAsync(ct);
        }
    }
}
