using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using ShopAggregate = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using PriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllCities;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBeans;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllEquipment;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBrewMethods;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllRoasters;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Moderation.Application.Services;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Features.Review.SendReviewToModeration;
using CoffeePeek.Moderation.Application.Features.Shop.CreateShop;
using CoffeePeek.Moderation.Application.Features.ShopChangeRequests;
using CoffeePeek.ModerationService.Controllers;
using CoffeePeek.Shops.Application.Features.CheckIn.CreateCheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shared.Auth.Constants;
using System.Text.Json.Nodes;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Account.Application.Mapper;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.PublicAddresses;
using CoffeePeek.Account.Persistence.Repositories;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Auth.Extensions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shared.Web.Handlers;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsOptions = Microsoft.Extensions.Options.Options;
using Moq;
using Wolverine;
using AccountFlags = CoffeePeek.Account.Application.Common.Options.PublicAddressOptions;
using ShopsFlags = CoffeePeek.Shops.Application.PublicAddresses.PublicAddressOptions;

namespace CoffeePeek.Account.Infrastructure.Tests;

/// <summary>Real HTTP, MVC and production YARP routes; synthetic EF data and an in-process command dispatcher.
/// PostgreSQL transactions and constraints are covered separately by the LivePostgres suite.</summary>
public sealed class PublicAddressGatewaySmokeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProductionRoutes_ReturnAddressesInOneResponseAndKeepGuidsInternal()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var accountFlags = new AccountFlags { Enabled = true };
        var shopsFlags = new ShopsFlags();
        AccountDbContext Account() => new(new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase(database + "-account", root).Options, MsOptions.Create(accountFlags));
        ShopsDbContext Shops() => new(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(database + "-shops", root).Options, MsOptions.Create(shopsFlags));

        var user = User.CreateExternal("private@example.com", "Google", "smoke", "Ivan.Petrov");
        var blocked = User.CreateExternal("blocked@example.com", "Google", "blocked", "Boris.Blocked");
        var deleted = User.CreateExternal("deleted@example.com", "Google", "deleted", "Dmitry.Deleted");
        await using (var db = Account())
        {
            db.Users.AddRange(user, blocked, deleted);
            await db.SaveChangesAsync(Ct);
            user.UpdateUsername(Username.Create("Petr"));
            blocked.SetBlocked(true);
            deleted.SetSoftDelete();
            await db.SaveChangesAsync(Ct);
        }
        var city = new City("Minsk");
        await using (var db = Shops()) { db.Cities.Add(city); await db.SaveChangesAsync(Ct); }
        var shop = new ShopAggregate(Guid.NewGuid(), "26", null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetDraftLocation(city.Id, "October 16");
        var bean = new CoffeeBean("Arabica");
        var equipment = new Equipment("La Marzocco", "Linea", new EquipmentCategory());
        var method = new BrewMethod("V60", BrewMethodCategory.Gravity);
        var tag = ShopTag.Create("dog-friendly", "Dogs welcome");
        var roaster = new Roaster("Beans Roastery");
        await using (var db = Shops())
        {
            db.AddRange(shop, bean, equipment, method, tag, roaster);
            db.Entry(equipment.Category).Property(nameof(EquipmentCategory.Name)).CurrentValue = "Espresso machines";
            await db.SaveChangesAsync(Ct);
        }
        var internalIds = new[] { shop.Id, city.Id, bean.Id, equipment.Id, method.Id, tag.Id, roaster.Id, user.Id };
        var summary = new ShortShopDto
        {
            Id = shop.Id, CityId = city.Id, Name = shop.Name, Photos = [],
            Beans = [new CoffeeBeansDto { Id = bean.Id, Name = bean.Name }],
            Roasters = [new RoasterDto { Id = roaster.Id, Name = roaster.Name }],
            Equipments = [new EquipmentDto { Id = equipment.Id, Name = equipment.Name, Brand = equipment.Brand, Model = equipment.ModelName }],
            BrewMethods = [new BrewMethodDto { Id = method.Id, Name = method.Name }]
        };
        var review = new ReviewDto { Id = Guid.NewGuid(), UserId = user.Id, CoffeeShopId = shop.Id,
            Username = "Petr", Comment = "Lovely coffee", Rating = new RatingDto { Place = 5, Coffee = 5, Service = 5 } };
        var detail = new CoffeeShopDetailsDto { Id = shop.Id, CityId = city.Id, Name = shop.Name, Photos = [],
            CoffeeBeans = summary.Beans, Roasters = summary.Roasters, Equipments = summary.Equipments,
            BrewMethods = summary.BrewMethods, Reviews = [review], Tags = [new ShopTagDto(tag.Id, tag.Slug, tag.Name, null, 0)] };
        var queries = new Mock<ICoffeeShopQueries>();
        SearchCoffeeShopsQuery? capturedSearch = null;
        queries.Setup(x => x.Search(It.IsAny<SearchCoffeeShopsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<SearchCoffeeShopsQuery, CancellationToken>((q, _) => capturedSearch = q)
            .ReturnsAsync((new[] { summary }, 1));
        accountFlags.Enabled = false;

        var bus = new Mock<IMessageBus>();
        bus.Setup(x => x.InvokeAsync<Response<PublicUserProfileResponse>>(It.IsAny<object>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Returns(async (object command, CancellationToken ct, TimeSpan? _) =>
            {
                await using var db = Account();
                return await new GetPublicUserProfileHandler().Handle((GetPublicUserProfileCommand)command,
                    new UserRepository(db), MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions
                    { PublicEndpoint = "http://localhost" }), ct);
            });

        bus.Setup(x => x.InvokeAsync<Response<GetCoffeeShopResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetCoffeeShopResponse>.Success(new GetCoffeeShopResponse(detail)));
        bus.Setup(x => x.InvokeAsync<Response<GetCitiesResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetCitiesResponse>.Success(new GetCitiesResponse([new() { Id = city.Id, Name = city.Name }])));
        bus.Setup(x => x.InvokeAsync<Response<GetAllCoffeeBeansResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetAllCoffeeBeansResponse>.Success(new(summary.Beans)));
        bus.Setup(x => x.InvokeAsync<Response<GetAllEquipmentResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetAllEquipmentResponse>.Success(new(summary.Equipments)));
        bus.Setup(x => x.InvokeAsync<Response<GetAllBrewMethodsResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetAllBrewMethodsResponse>.Success(new(summary.BrewMethods)));
        bus.Setup(x => x.InvokeAsync<Response<GetAllRoastersResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetAllRoastersResponse>.Success(new(summary.Roasters)));
        bus.Setup(x => x.InvokeAsync<Response<RoasterDetailsDto>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<RoasterDetailsDto>.Success(new() { Id = roaster.Id, Name = roaster.Name,
                Shops = [new() { Id = shop.Id, Name = shop.Name }] }));
        bus.Setup(x => x.InvokeAsync<Response<GetShopsInBoundsResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetShopsInBoundsResponse>.Success(new([new() { Id = shop.Id, Title = shop.Name, Latitude = 53.9m, Longitude = 27.56m }])));
        SendReviewToModerationCommand? capturedReview = null;
        bus.Setup(x => x.InvokeAsync<CreateEntityResponse>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Callback<object, CancellationToken, TimeSpan?>((command, _, _) => capturedReview = (SendReviewToModerationCommand)command)
            .ReturnsAsync(CreateEntityResponse.Success(entityId: Guid.NewGuid()));
        SendCoffeeShopToModerationCommand? capturedSubmission = null;
        bus.Setup(x => x.InvokeAsync<Response<SendCoffeeShopToModerationResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Callback<object, CancellationToken, TimeSpan?>((command, _, _) => capturedSubmission = (SendCoffeeShopToModerationCommand)command)
            .ReturnsAsync(Response<SendCoffeeShopToModerationResponse>.Success(new(Guid.NewGuid(), "pending", false)));
        var changeId = Guid.NewGuid();
        bus.Setup(x => x.InvokeAsync<Response<ShopChangeRequestDto>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((object command, CancellationToken _, TimeSpan? _) =>
            {
                var change = (CreateShopChangeRequestCommand)command;
                change.UserId.Should().Be(user.Id);
                change.ShopId.Should().Be(shop.Id);
                change.Payload.TagIds.Should().Equal(tag.Id);
                return Response<ShopChangeRequestDto>.Success(new(changeId, change.ShopId, change.UserId,
                    change.Section, change.Payload, ModerationStatus.Pending, null, null, null, DateTime.UtcNow, null));
            });
        CreateCheckInCommand? capturedCheckIn = null;
        bus.Setup(x => x.InvokeAsync<Response<CreateCheckInResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Callback<object, CancellationToken, TimeSpan?>((command, _, _) => capturedCheckIn = (CreateCheckInCommand)command)
            .ReturnsAsync(Response<CreateCheckInResponse>.Success(new(Guid.NewGuid())));
        var accountBuilder = ControllerHost(typeof(PublicUserAddressesController));
        accountBuilder.Services.AddScoped(_ => Account());
        accountBuilder.Services.AddScoped<IProfileAddressService, ProfileAddressService>();
        accountBuilder.Services.AddSingleton(bus.Object);
        accountBuilder.Services.AddSingleton(Mock.Of<IUserContext>());
        await using var account = accountBuilder.Build();
        MapControllers(account, "account");
        await account.StartAsync(Ct);

        var shopsBuilder = ControllerHost(typeof(PublicCityAddressesController));
        shopsBuilder.Services.AddScoped(_ => Shops());
        shopsBuilder.Services.AddScoped<CoffeePeek.Shops.Application.PublicAddresses.IPublicAddressService, PublicAddressService>();
        shopsBuilder.Services.AddScoped<IQueryCityRepository, QueryCityRepository>();
        shopsBuilder.Services.AddSingleton(bus.Object);
        shopsBuilder.Services.AddSingleton(queries.Object);
        shopsBuilder.Services.AddSingleton(Mock.Of<IQueryCheckInRepository>());
        shopsBuilder.Services.AddScoped<IProfileAddressLookup, CoffeePeek.Shops.Infrastructure.Account.AccountProfileAddressLookup>();
        shopsBuilder.Services.AddScoped<PublicAddressHttpClient>();
        shopsBuilder.Services.AddHttpClient("account-user-lookup", c => c.BaseAddress = new Uri(Address(account)));
        shopsBuilder.Services.AddScoped<PublicDiscoveryService>();
        await using var shops = shopsBuilder.Build();
        MapControllers(shops, "shops");
        await shops.StartAsync(Ct);

        var moderationBuilder = ControllerHost(typeof(ModerationReviewsController));
        moderationBuilder.Services.AddSingleton(bus.Object);
        moderationBuilder.Services.AddScoped<PublicAddressHttpClient>();
        moderationBuilder.Services.AddHttpClient("shops-roaster-lookup", c => c.BaseAddress = new Uri(Address(shops)));
        moderationBuilder.Services.AddScoped<IPublicAddressLookup, CoffeePeek.Moderation.Infrastructure.Shops.ShopsPublicAddressLookup>();
        moderationBuilder.Services.AddScoped<PublicModerationService>();
        await using var moderation = moderationBuilder.Build();
        MapControllers(moderation, "moderation");
        await moderation.StartAsync(Ct);

        var gatewayBuilder = Host();
        gatewayBuilder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway-smoke.json"));
        foreach (var cluster in gatewayBuilder.Configuration.GetSection("ReverseProxy:Clusters").GetChildren())
            gatewayBuilder.Configuration[$"ReverseProxy:Clusters:{cluster.Key}:Destinations:destination1:Address"] =
                cluster.Key == "account-cluster" ? Address(account) : cluster.Key == "moderation-cluster" ? Address(moderation) : Address(shops);
        gatewayBuilder.Services.Configure<GatewayAuthOptions>(gatewayBuilder.Configuration.GetSection("GatewayAuth"));
        gatewayBuilder.Services.AddServiceDiscovery();
        gatewayBuilder.Services.AddAuthorization(o =>
        {
            foreach (var policy in new[] { "Authenticated", "Admin", "Moderator", "Owner", "AppReleaseAutomationPolicy" })
                o.AddPolicy(policy, p => p.RequireAuthenticatedUser());
        });
        gatewayBuilder.Services.AddGatewayRateLimiting().AddGatewayProxy(gatewayBuilder.Configuration);
        await using var gateway = gatewayBuilder.Build();
        gateway.UseRouting();
        gateway.UseAuthorization();
        gateway.UseRateLimiter();
        gateway.MapReverseProxy();
        await gateway.StartAsync(Ct);
        using var http = new HttpClient { BaseAddress = new Uri(Address(gateway)), Timeout = TimeSpan.FromSeconds(15) };

        // This path also matches shops-users-reviews-route: the literal by-slug route must win.
        (await Get("/api/Users/by-slug/reviews", HttpStatusCode.ServiceUnavailable, "account"))!["errorCode"]
            .Should().NotBeNull();
        await Get($"/api/Users/{user.Id}", HttpStatusCode.ServiceUnavailable, "account");
        using (var reviews = await http.GetAsync("/api/users/petr/reviews", Ct))
        {
            reviews.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            reviews.Headers.GetValues("X-Smoke-Service").Should().ContainSingle().Which.Should().Be("shops");
        }
        await Get("/api/Catalogs/cities/by-slug/minsk", HttpStatusCode.ServiceUnavailable, "shops");
        shopsFlags.Enabled = true;
        await Get("/api/Catalogs/cities/by-slug/minsk", HttpStatusCode.NotFound, "shops");
        await Get("/api/CoffeeShops", HttpStatusCode.ServiceUnavailable, "shops");
        await using (var db = Shops())
            foreach (var kind in Enum.GetValues<PublicAddressKind>())
                await new PublicAddressService(db).BackfillAsync(kind, null, 100, false, Guid.NewGuid(), Ct);
        var cityEnvelope = await Get("/api/Catalogs/cities/by-slug/MINSK", HttpStatusCode.OK, "shops");
        cityEnvelope!["address"]!["canonicalPath"]!.GetValue<string>().Should().Be("/cities/minsk");
        NoInternalIds(cityEnvelope);
        var list = await Get("/api/CoffeeShops?city=minsk&roasters=beans-roastery&beans=arabica&equipments=la-marzocco-linea&brewMethods=v60&tags=dog-friendly", HttpStatusCode.OK, "shops");
        NoInternalIds(list);
        list!["data"]!["coffeeShops"]![0]!["address"]!["slug"]!.GetValue<string>().Should().Be("26-october-16");
        list["data"]!["coffeeShops"]![0]!["beans"]![0]!["slug"]!.GetValue<string>().Should().Be("arabica");
        capturedSearch!.CityId.Should().Be(city.Id);
        capturedSearch.Roasters.Should().Equal(roaster.Id);
        capturedSearch.Beans.Should().Equal(bean.Id);
        capturedSearch.Equipments.Should().Equal(equipment.Id);
        capturedSearch.BrewMethods.Should().Equal(method.Id);
        capturedSearch.Tags.Should().Equal(tag.Id);
        foreach (var catalog in new[] { "cities", "beans", "equipments", "brew-methods", "roasters" })
        {
            var catalogJson = await Get("/api/Catalogs/" + catalog, HttpStatusCode.OK, "shops");
            NoInternalIds(catalogJson);
            catalogJson!["data"]!.AsArray().Should().HaveCount(1);
        }
        NoInternalIds(await Get("/api/Roasters/beans-roastery", HttpStatusCode.OK, "shops"));
        var map = await Get("/api/Map?minLat=53&minLon=27&maxLat=54&maxLon=28", HttpStatusCode.OK, "shops");
        NoInternalIds(map);
        map!["data"]!["shops"]![0]!["address"]!["slug"]!.GetValue<string>().Should().Be("26-october-16");
        await Get($"/api/CoffeeShops?beans={bean.Id}", HttpStatusCode.BadRequest, "shops");
        await Get("/api/CoffeeShops?beans=unknown", HttpStatusCode.NotFound, "shops");
        await Get($"/api/CoffeeShops/{shop.Id}", HttpStatusCode.BadRequest, "shops");
        using (var oldLookup = await http.GetAsync($"/api/CoffeeShops/{shop.Id}/public-address", Ct))
            oldLookup.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var internalLookup = await http.GetAsync($"/internal/public-addresses/users?ids={user.Id}", Ct))
            internalLookup.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var internalHttp = new HttpClient { BaseAddress = new Uri(Address(account)) })
        {
            using var denied = await internalHttp.GetAsync($"/internal/public-addresses/users?ids={user.Id}", Ct);
            denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        accountFlags.Enabled = true;
        var alias = await Get("/api/Users/by-slug/IVAN-PETROV", HttpStatusCode.OK, "account");
        alias!["address"]!["canonicalPath"]!.GetValue<string>().Should().Be("/users/petr");
        alias["address"]!["isAlias"]!.GetValue<bool>().Should().BeTrue();
        alias["address"]!["revision"]!.GetValue<long>().Should().Be(2);
        alias["data"]!["userName"]!.GetValue<string>().Should().Be("Petr");
        alias["data"]!.AsObject().ContainsKey("email").Should().BeFalse();
        var canonical = await Get("/api/Users/by-slug/petr", HttpStatusCode.OK, "account");
        canonical!["address"]!["isAlias"]!.GetValue<bool>().Should().BeFalse();
        // Browsing more than 20 profiles must not consume the login/token-refresh bucket.
        for (var i = 0; i < 21; i++) await Get("/api/Users/by-slug/petr", HttpStatusCode.OK, "account");
        NoInternalIds(alias);
        var shopDetail = await Get("/api/CoffeeShops/by-slug/26-october-16", HttpStatusCode.OK, "shops");
        NoInternalIds(shopDetail);
        shopDetail!["data"]!["reviews"]![0]!["author"]!["slug"]!.GetValue<string>().Should().Be("petr");
        shopDetail["data"]!["reviews"]![0]!["id"]!.GetValue<Guid>().Should().Be(review.Id);
        using (var authenticated = new HttpClient { BaseAddress = new Uri(Address(shops)) })
        {
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "http-smoke-internal-secret-at-least-32-characters");
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, user.Id.ToString());
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserName, "Petr");
            using var created = await authenticated.PostAsJsonAsync("/api/CheckIns", new
            { shop = "26-october-16", isPublic = false, visitedAt = DateTime.UtcNow, rating = new { place = 5, coffee = 5, service = 5 } }, Ct);
            created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Ct));
            capturedCheckIn!.CoffeeShopId.Should().Be(shop.Id);
            capturedCheckIn.UserId.Should().Be(user.Id);
            using var invalid = await authenticated.PostAsJsonAsync("/api/CheckIns", new
            { shop = shop.Id.ToString(), isPublic = false, visitedAt = DateTime.UtcNow, rating = new { place = 5, coffee = 5, service = 5 } }, Ct);
            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        using (var authenticated = new HttpClient { BaseAddress = new Uri(Address(moderation)) })
        {
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "http-smoke-internal-secret-at-least-32-characters");
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, user.Id.ToString());
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserName, "Petr");
            using var reviewCreated = await authenticated.PostAsJsonAsync("/api/ModerationReviews", new
                { shop = "26-october-16", comment = "Great coffee", ratingCoffee = 4, ratingPlace = 5, ratingService = 4 }, Ct);
            reviewCreated.StatusCode.Should().Be(HttpStatusCode.OK, await reviewCreated.Content.ReadAsStringAsync(Ct));
            capturedReview!.ShopId.Should().Be(shop.Id);
            capturedReview.UserId.Should().Be(user.Id);
            capturedReview.EffectiveRating.Coffee.Should().Be(4);
            using var shopSubmitted = await authenticated.PostAsJsonAsync("/api/ModerationShops", new
                { name = "New cafe", address = "October 18", city = "minsk", beans = new[] { "arabica" },
                  equipments = new[] { "la-marzocco-linea" }, roasters = new[] { "beans-roastery" }, brewMethods = new[] { "v60" } }, Ct);
            shopSubmitted.StatusCode.Should().Be(HttpStatusCode.Created, await shopSubmitted.Content.ReadAsStringAsync(Ct));
            capturedSubmission!.Name.Should().Be("New cafe");
            capturedSubmission.CityId.Should().Be(city.Id);
            capturedSubmission.CoffeeBeanIds.Should().Equal(bean.Id);
            capturedSubmission.EquipmentIds.Should().Equal(equipment.Id);
            capturedSubmission.RoasterIds.Should().Equal(roaster.Id);
            capturedSubmission.BrewMethodIds.Should().Equal(method.Id);
            using var changeCreated = await authenticated.PostAsJsonAsync("/api/ShopChangeRequests", new
                { shop = "26-october-16", section = (int)ShopChangeSection.Tags, payload = new { tags = new[] { "dog-friendly" } } }, Ct);
            changeCreated.StatusCode.Should().Be(HttpStatusCode.Created, await changeCreated.Content.ReadAsStringAsync(Ct));
            var changeJson = JsonNode.Parse(await changeCreated.Content.ReadAsStringAsync(Ct));
            NoInternalIds(changeJson);
            changeJson!["data"]!["payload"]!["tags"]![0]!.GetValue<string>().Should().Be("dog-friendly");
            changeJson["data"]!["id"]!.GetValue<Guid>().Should().Be(changeId);
            using var nullSlug = await authenticated.PostAsJsonAsync("/api/ModerationShops", new
                { name = "Bad cafe", address = "October 18", city = "minsk", beans = new string?[] { null } }, Ct);
            nullSlug.StatusCode.Should().Be(HttpStatusCode.BadRequest, await nullSlug.Content.ReadAsStringAsync(Ct));
        }
        var unknown = await Get("/api/Users/by-slug/nobody", HttpStatusCode.NotFound, "account");
        foreach (var slug in new[] { "boris-blocked", "dmitry-deleted" })
        {
            var hidden = await Get("/api/Users/by-slug/" + slug, HttpStatusCode.NotFound, "account");
            hidden!.AsObject().Remove("traceId");
            unknown!.AsObject().Remove("traceId");
            JsonNode.DeepEquals(hidden, unknown).Should().BeTrue();
        }
        await Get($"/api/Users/{blocked.Id}", HttpStatusCode.BadRequest, "account");
        accountFlags.Enabled = false;
        shopsFlags.Enabled = false;
        await Get("/api/Users/by-slug/petr", HttpStatusCode.ServiceUnavailable, "account");
        await Get($"/api/Users/{user.Id}", HttpStatusCode.ServiceUnavailable, "account");
        await Get("/api/Catalogs/cities/by-slug/minsk", HttpStatusCode.ServiceUnavailable, "shops");

        void NoInternalIds(JsonNode? node)
        {
            var json = node!.ToJsonString();
            foreach (var id in internalIds) json.Should().NotContain(id.ToString());
            json.Should().NotContain("entityId").And.NotContain("cityId").And.NotContain("userId").And.NotContain("shopId").And.NotContain("coffeeShopId");
        }

        async Task<JsonNode?> Get(string path, HttpStatusCode status, string service)
        {
            using var response = await http.GetAsync(path, Ct);
            var body = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.Should().Be(status, $"{path}: {body}");
            response.Headers.GetValues("X-Smoke-Service").Should().ContainSingle().Which.Should().Be(service);
            if (status == HttpStatusCode.OK && (path.Contains("by-slug") || path.Contains("public-address")))
                response.Headers.CacheControl?.NoStore.Should().BeTrue();
            return JsonNode.Parse(body);
        }
    }

    private static WebApplicationBuilder Host()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        return builder;
    }

    private static WebApplicationBuilder ControllerHost(Type controller)
    {
        var builder = Host();
        builder.Services.AddControllersModule();
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.ApplicationParts.Add(new AssemblyPart(controller.Assembly));
        });
        builder.Services.AddAuthorization();
        builder.Configuration["GatewayAuth:SecretKey"] = "http-smoke-internal-secret-at-least-32-characters";
        builder.Services.AddHeaderUserContext(builder.Configuration);
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        return builder;
    }

    private static void MapControllers(WebApplication app, string service)
    {
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() => { context.Response.Headers["X-Smoke-Service"] = service; return Task.CompletedTask; });
            await next(context);
        });
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", () => "healthy");
    }

    private static string Address(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
}
