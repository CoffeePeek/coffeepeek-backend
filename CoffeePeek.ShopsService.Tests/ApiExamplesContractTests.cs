using System.Text.Json;
using System.Text.Json.Nodes;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.Catalogs.GetMenuDrinks;
using CoffeePeek.Shops.Application.Features.Menu;
using CoffeePeek.Shops.Application.PublicAddresses;
using IQueryCheckInRepository = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.IQueryCheckInRepository;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Seed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using DomainCategory = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.CoffeeDrinkCategory;

namespace CoffeePeek.ShopsService.Tests;

public class ApiExamplesContractTests
{
    private static readonly Guid ShopId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AuthorId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid PendingId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task SlugCatalogAndCompleteExamplesMatchCurrentMvcContracts()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = CreateJsonOptions();
        var catalog = CreateCatalog();
        Assert.Equal(14, catalog.Length);
        for (var i = 0; i < catalog.Length; i++)
        {
            var mapped = JsonSerializer.SerializeToElement(catalog[i], options);
            Assert.False(mapped.TryGetProperty("id", out _), "The current public catalog identifies drinks by slug.");
            Assert.Equal(CoffeeDrinkSeed.Rows.OrderBy(row => row.Sort).ElementAt(i).Slug, mapped.GetProperty("slug").GetString());
        }
        var cappuccino = JsonSerializer.SerializeToElement(catalog.Single(drink => drink.Slug == "cappuccino"), options);
        Assert.Equal("cappuccino", cappuccino.GetProperty("slug").GetString());
        Assert.Equal("shop:menu:drinks", CacheKey.Shop.MenuDrinks().Key);
        Assert.Equal(TimeSpan.FromHours(6), CacheKey.Shop.MenuDrinks().DefaultTtl);

        var exampleDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../docs/api/examples"));
        foreach (var (fileName, response) in await CreateExamplesAsync(ct))
        {
            var expected = JsonSerializer.Serialize(response, response.GetType(), options);
            Assert.DoesNotContain(ShopId.ToString(), expected);
            Assert.DoesNotContain(AuthorId.ToString(), expected);
            foreach (var identity in new[] { "userId", "shopId", "coffeeShopId", "drinkDefinitionId" })
                Assert.DoesNotContain($"\"{identity}\"", expected);
            var fixture = File.ReadAllText(Path.Combine(exampleDirectory, fileName));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(fixture)),
                $"{fileName} must contain the complete actual MVC-serialized response.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersModule();
        using var provider = services.BuildServiceProvider();
        return new JsonSerializerOptions(provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions)
        {
            WriteIndented = true
        };
    }

    private static CoffeePeek.Contract.Dtos.Menu.CoffeeDrinkDefinitionDto[] CreateCatalog() => CoffeeDrinkSeed.Rows
        .OrderBy(row => row.Sort)
        .Select(row => ShopMenuDtoFactory.ToDto(CoffeeDrinkDefinition.CreateWithId(row.Id, row.Slug,
            row.NameRu, row.NameEn, (DomainCategory)row.Category, row.Aliases, row.Sort)))
        .ToArray();

    private static async Task<Dictionary<string, object>> CreateExamplesAsync(CancellationToken ct)
    {
        var photoId = Guid.Parse("66666666-6666-4666-8666-666666666666");
        var cappuccino = CreateCatalog().Single(drink => drink.Slug == "cappuccino");
        var pending = new UnifiedCheckInDto
        {
            Id = PendingId,
            UserId = AuthorId,
            Username = "example_author",
            ShopId = ShopId,
            ShopName = "Example Coffee",
            Text = "A balanced cappuccino and friendly service.",
            Rating = new RatingDto { Coffee = 5, Service = 4, Place = 4 },
            VisitedAt = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc),
            Visibility = CheckInVisibility.Public,
            ModerationState = CheckInModerationState.Pending,
            ContentRevision = 1,
            Drink = new CheckInDrinkDto(cappuccino.Slug, null, cappuccino.NameRu, cappuccino.NameEn),
            Photos = [new CheckInPhotoDto(photoId, "cappuccino.jpg", "image/jpeg",
                "check-ins/example-cappuccino.jpg", 1200000, 0,
                $"/api/v1/check-ins/{PendingId}/photos/{photoId}")]
        };
        var privateVisit = pending with
        {
            Id = Guid.Parse("55555555-5555-4555-8555-555555555555"),
            Text = "A quiet early visit.",
            Rating = new RatingDto { Coffee = 4, Service = 4, Place = 5 },
            VisitedAt = new DateTime(2026, 10, 7, 6, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 10, 7, 6, 0, 0, DateTimeKind.Utc),
            Visibility = CheckInVisibility.Private,
            ModerationState = CheckInModerationState.NotSubmitted,
            Drink = null,
            Photos = []
        };
        var approved = pending with
        {
            Id = Guid.Parse("44444444-4444-4444-8444-444444444444"),
            Text = "Good coffee during yesterday's visit.",
            Rating = new RatingDto { Coffee = 4, Service = 5, Place = 3 },
            VisitedAt = new DateTime(2026, 10, 6, 21, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 10, 6, 21, 0, 0, DateTimeKind.Utc),
            ModerationState = CheckInModerationState.Approved,
            Drink = null,
            Photos = [],
            HelpfulCount = 2
        };
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(x => x.GetForReadAsync(PublicAddressKind.CoffeeShop, It.IsAny<Guid[]>(), ct))
            .ReturnsAsync([new PublicAddressMetadata(ShopId, "example-coffee", "/coffee-shops/example-coffee", 1)]);
        var profiles = new Mock<IProfileAddressLookup>();
        profiles.Setup(x => x.GetByIdsAsync(It.IsAny<Guid[]>(), ct))
            .ReturnsAsync([new PublicAddressMetadata(AuthorId, "example-author", "/users/example-author", 1)]);
        var discovery = new PublicDiscoveryService(addresses.Object, profiles.Object, Mock.Of<ICoffeeShopQueries>(),
            Mock.Of<IQueryCheckInRepository>(), Mock.Of<IMessageBus>());
        var visits = await discovery.CheckInsAsync([pending, privateVisit, approved], ct);
        Assert.Equal(CheckInVisibility.Public, visits[0].Visibility);
        Assert.Equal(CheckInModerationState.Pending, visits[0].ModerationState);
        Assert.Equal(CheckInVisibility.Private, visits[1].Visibility);
        Assert.Equal(CheckInModerationState.NotSubmitted, visits[1].ModerationState);
        Assert.Equal(CheckInVisibility.Public, visits[2].Visibility);
        Assert.Equal(CheckInModerationState.Approved, visits[2].ModerationState);
        var photo = Assert.Single(visits[0].Photos);
        Assert.Equal($"/api/v1/check-ins/{PendingId}/photos/{photoId}", photo.Url);
        return new Dictionary<string, object>
        {
            ["check-in.json"] = Response<PublicCheckInDto>.Success(visits[0]),
            ["check-ins-mine.json"] = Response<PublicCanonicalOwnerCheckInPage>.Success(new(visits, 3)),
            ["check-ins-feed.json"] = Response<PublicCanonicalCheckInPage>.Success(new([visits[2]], null)),
            ["menu-drinks.json"] = Response<GetMenuDrinksResponse>.Success(new GetMenuDrinksResponse(CreateCatalog()))
        };
    }
}
