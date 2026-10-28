using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Infrastructure.Media;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using DomainGrind = CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate.ShelfBagGrind;
using DomainRoastPurpose = CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate.ShelfBagRoastPurpose;

namespace CoffeePeek.ShopsService.Tests;

/// <summary>Keeps docs/api/shelf-client.md, the MVC JSON contract and the routes in step.</summary>
public class ShelfContractTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Doc() => File.ReadAllText(Path.Combine(Root, "docs/api/shelf-client.md"));

    private static string[] JsonBlocks() => Regex.Matches(Doc(), "```(?:json|http)\\n(.*?)```", RegexOptions.Singleline)
        .Select(m => m.Groups[1].Value)
        .Select(block => block.Contains("HTTP/") || block.StartsWith("POST ") || block.StartsWith("PUT ")
            ? block[(block.IndexOf("\n\n", StringComparison.Ordinal) + 2)..] : block)
        .ToArray();

    private static string Block(string marker) => JsonBlocks().Single(b => b.Contains(marker, StringComparison.Ordinal));

    private static JsonSerializerOptions MvcJson()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersModule();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
    }

    [Fact]
    public void DocumentedBagIsExactlyWhatTheMappingSerializes()
    {
        var options = MvcJson();
        var expected = JsonNode.Parse(Block("\"status\": \"OnShelf\""))!;
        var productId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var bagId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var photoId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var addedAt = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
        var bag = ShelfBag.Create(bagId, Guid.NewGuid(),
            new ShelfBagDetails(productId, "Nut'n'Berry", "Sibaristica", 250, DomainGrind.Beans, DomainRoastPurpose.Espresso,
                new DateOnly(2026, 10, 12)),
            new ShelfBagPhoto(photoId, $"shelf-bags/{photoId:N}.jpg", "image/jpeg"), addedAt);
        new ShelfLineup(bag.UserId, []).AddToFront(bag);

        var dto = ShelfBagMapping.ToDto(bag, new Dictionary<Guid, string> { [productId] = "nut-n-berry" });
        var actual = JsonSerializer.SerializeToNode(dto, options)!;

        Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
        Assert.Equal("Espresso", actual["roastPurpose"]!.GetValue<string>());
        Assert.DoesNotContain("userId", actual.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", actual.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        var roundTrip = JsonSerializer.Deserialize<ShelfBagDto>(expected.ToJsonString(), options)!;
        Assert.Equal(dto, roundTrip);
    }

    [Fact]
    public void DocumentedRequestsBindToTheContracts()
    {
        var options = MvcJson();
        var create = JsonSerializer.Deserialize<CreateShelfBagRequest>(Block("\"coffeeProductSlug\": \"nut-n-berry\",\n  \"roasterName\""), options)!;
        Assert.Equal("shelf-bags/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg", create.Photo!.StorageKey);
        Assert.Equal("nut-n-berry", create.CoffeeProductSlug);
        Assert.Null(create.RoasterName);
        Assert.Equal(250, create.WeightGrams);
        Assert.Equal(CoffeePeek.Contract.Enums.ShelfBagGrind.Beans, create.Grind);
        Assert.Equal(CoffeePeek.Contract.Enums.ShelfBagRoastPurpose.Espresso, create.RoastPurpose);
        Assert.Equal(new DateOnly(2026, 10, 12), create.RoastedOn);

        var finish = JsonSerializer.Deserialize<FinishShelfBagRequest>(Block("\"buyAgain\": true }"), options)!;
        Assert.Equal(4, finish.Rating);
        Assert.True(finish.BuyAgain);
        Assert.Equal(2, JsonSerializer.Deserialize<RestoreShelfBagRequest>(Block("{ \"position\": 2 }"), options)!.Position);
        Assert.Contains("\"bagIds\"", Block("\"bagIds\""));
        Assert.NotNull(typeof(ReorderShelfRequest).GetProperty(nameof(ReorderShelfRequest.BagIds)));
        Assert.NotNull(typeof(UpdateShelfBagRequest).GetProperty(nameof(UpdateShelfBagRequest.CoffeeProductId)));
        Assert.Null(typeof(UpdateShelfBagRequest).GetProperty("Status"));
        Assert.Null(typeof(UpdateShelfBagRequest).GetProperty("Rating"));
    }

    [Fact]
    public void EveryDocumentedRouteIsAnOwnerOnlyNoStoreActionAndRoutedByTheGateway()
    {
        var type = typeof(ShelfController);
        Assert.Equal("api/v1/shelf", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.DoesNotContain(type.GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);

        var routes = type.GetMethods().SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()
                .Select(a => $"{a.HttpMethods.Single()} /api/v1/shelf{(string.IsNullOrEmpty(a.Template) ? "" : "/" + a.Template)}"))
            .Select(r => r.Replace(":guid", "")).OrderBy(r => r).ToArray();
        var documented = Regex.Matches(Doc(), @"^\| `(GET|POST|PUT|DELETE)` \| `([^`?]+)", RegexOptions.Multiline)
            .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}").OrderBy(r => r).ToArray();
        Assert.Equal(9, documented.Length);
        Assert.Equal(documented, routes);

        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "CoffeePeek.Gateway/appsettings.json")));
        var gateway = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").GetProperty("shops-shelf-route");
        Assert.Equal("shops-cluster", gateway.GetProperty("ClusterId").GetString());
        Assert.Equal("Authenticated", gateway.GetProperty("AuthorizationPolicy").GetString());
        Assert.Equal("/api/v1/shelf/{**remainder}", gateway.GetProperty("Match").GetProperty("Path").GetString());
    }

    [Fact]
    public async Task ReadsAreScopedToTheSignedInOwnerAndArchiveInputIsBounded()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(owner);
        var queries = new Mock<IShelfQueries>();
        queries.Setup(q => q.GetShelfAsync(owner, ct)).ReturnsAsync([]);
        queries.Setup(q => q.GetArchiveAsync(owner, 1, 100, "nut", ct)).ReturnsAsync(new ShelfArchivePage([], 0));
        var controller = new ShelfController(Mock.Of<IMessageBus>(), user.Object, queries.Object,
            new MediaShelfBagPhotoClient(Mock.Of<IHttpClientFactory>(), Options.Create(new CoffeePeek.Shared.Auth.Options.GatewayAuthOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaShelfBagPhotoClient>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        Assert.IsType<OkObjectResult>(await controller.List(ct));
        Assert.IsType<OkObjectResult>(await controller.Archive(0, 500, "nut", ct));
        Assert.IsType<BadRequestObjectResult>(await controller.Archive(1, 20, new string('q', 101), ct));
        Assert.IsType<NotFoundResult>(await controller.Photo(Guid.NewGuid(), Guid.NewGuid(), ct));
        queries.Verify(q => q.GetArchiveAsync(owner, 1, 100, "nut", ct), Times.Once);
        queries.Verify(q => q.GetArchiveAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.Is<string?>(s => s!.Length > 100),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
