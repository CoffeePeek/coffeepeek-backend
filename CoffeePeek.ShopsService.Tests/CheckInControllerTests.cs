using System.Reflection;
using System.Text.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.PublicAddresses;
using IQueryCheckInRepository = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.IQueryCheckInRepository;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class CheckInControllerTests
{
    [Fact]
    public void GatewayReachabilityAndMutationPoliciesMatchTheVersionedContract()
    {
        var type = typeof(UnifiedCheckInsController);
        Assert.Equal("api/v1/check-ins", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        foreach (var name in new[] { "Create", "Mine", "Update", "Visibility", "Delete", "Helpful", "Unhelpful" })
            Assert.NotNull(type.GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>());
        foreach (var name in new[] { "Get", "Feed" })
            Assert.NotNull(type.GetMethod(name)!.GetCustomAttribute<AllowAnonymousAttribute>());
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "CoffeePeek.Gateway/appsettings.json")));
        var routes = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        var matching = routes.EnumerateObject().Single(r => r.Value.GetProperty("Match").GetProperty("Path")
            .GetString() == "/api/v1/check-ins/{**remainder}").Value;
        Assert.Equal("shops-cluster", matching.GetProperty("ClusterId").GetString());
    }

    [Fact]
    public void PublicRequestPreservesScoresAndDefaultsToPrivate()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = JsonSerializer.Deserialize<PublicCreateUnifiedCheckInRequest>("""
            {"coffeeShopSlug":"example-coffee","text":"Good coffee","rating":{"coffee":4,"service":3,"place":5},"userId":"22222222-2222-2222-2222-222222222222","username":"spoof","coffeeShopId":"11111111-1111-1111-1111-111111111111"}
            """, options)!;
        Assert.Equal("example-coffee", request.CoffeeShopSlug);
        Assert.Equal(4, request.Rating.Coffee);
        Assert.Equal(3, request.Rating.Service);
        Assert.Equal(5, request.Rating.Place);
        Assert.Equal(CheckInVisibility.Private, request.Visibility);
        Assert.Null(request.DrinkSlug);
        Assert.Null(request.CustomDrinkName);
        Assert.Null(request.Photos);
        foreach (var identity in new[] { "UserId", "Username", "CoffeeShopId", "DrinkDefinitionId" })
            Assert.Null(typeof(PublicCreateUnifiedCheckInRequest).GetProperty(identity));
    }

    [Theory]
    [InlineData("cappuccino", null)]
    [InlineData(null, "House drink")]
    [InlineData(null, null)]
    public async Task CreateResolvesShopSlugAndProjectsPublicAddresses(string? drinkSlug, string? customDrinkName)
    {
        var ct = TestContext.Current.CancellationToken;
        var shopId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var checkInId = Guid.NewGuid();
        var shop = new PublicAddressMetadata(shopId, "example-coffee", "/coffee-shops/example-coffee", 2);
        var author = new PublicAddressMetadata(authorId, "example-author", "/users/example-author", 3);
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(x => x.ResolveAsync(PublicAddressKind.CoffeeShop, shop.Slug, ct)).ReturnsAsync(shop);
        addresses.Setup(x => x.GetForReadAsync(PublicAddressKind.CoffeeShop,
            It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { shopId })), ct)).ReturnsAsync(new[] { shop });
        var profiles = new Mock<IProfileAddressLookup>();
        profiles.Setup(x => x.GetByIdsAsync(It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { authorId })), ct))
            .ReturnsAsync(new[] { author });
        var user = new Mock<IUserContext>();
        user.Setup(x => x.GetUserIdOrThrow()).Returns(authorId);
        user.Setup(x => x.GetUsernameOrThrow()).Returns("example_author");
        var request = new PublicCreateUnifiedCheckInRequest(shop.Slug, "Good coffee",
            new RatingDto { Coffee = 5, Service = 4, Place = 3 }, DrinkSlug: drinkSlug, CustomDrinkName: customDrinkName);
        var bus = DispatchProxy.Create<IMessageBus, ReviewReportApiContractTests.RecordingBus>();
        var recorder = (ReviewReportApiContractTests.RecordingBus)(object)bus;
        recorder.NextResponse = Response<UnifiedCheckInDto>.Success(new UnifiedCheckInDto
        {
            Id = checkInId, ShopId = shopId, UserId = authorId, Username = "example_author", ShopName = "Example Coffee",
            Text = request.Text, Rating = request.Rating, Visibility = request.Visibility,
            ModerationState = CheckInModerationState.NotSubmitted,
            Drink = drinkSlug is null && customDrinkName is null ? null : new CheckInDrinkDto(drinkSlug!, customDrinkName, "", "")
        });
        var discovery = new PublicDiscoveryService(addresses.Object, profiles.Object, Mock.Of<ICoffeeShopQueries>(),
            Mock.Of<IQueryCheckInRepository>(), bus);
        var controller = new UnifiedCheckInsController(bus, user.Object, Mock.Of<ICheckInQueries>(), discovery, profiles.Object);

        var result = Assert.IsType<OkObjectResult>(await controller.Create(request, ct));

        var command = Assert.IsType<CreateUnifiedCheckInCommand>(recorder.LastMessage);
        Assert.Equal(shopId, command.Request.CoffeeShopId);
        Assert.Equal(authorId, command.UserId);
        Assert.Equal("example_author", command.Username);
        Assert.Equal(drinkSlug, command.Request.DrinkSlug);
        Assert.Equal(customDrinkName, command.Request.CustomDrinkName);
        var response = Assert.IsType<Response<PublicCheckInDto>>(result.Value);
        Assert.Equal(checkInId, response.Data.Id);
        Assert.Equal(shop.Slug, response.Data.Shop!.Slug);
        Assert.Equal(author.Slug, response.Data.Author!.Slug);
        Assert.Equal(drinkSlug, response.Data.DrinkSlug);
        Assert.Equal(customDrinkName, response.Data.CustomDrinkName);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(shopId.ToString(), json);
        Assert.DoesNotContain(authorId.ToString(), json);
        foreach (var identity in new[] { "userId", "shopId", "coffeeShopId", "drinkDefinitionId" })
            Assert.DoesNotContain($"\"{identity}\"", json);
    }
}
