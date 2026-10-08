using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using ShopAggregate = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using PriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllCities;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBeans;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllEquipment;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBrewMethods;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Moderation.Application.Services;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Features.Shop.CreateShop;
using CoffeePeek.Moderation.Application.Features.ShopChangeRequests;
using CoffeePeek.ModerationService.Controllers;
using IQueryCheckInRepository = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.IQueryCheckInRepository;
using CoffeePeek.Shops.Application.Features.CheckIn;
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
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
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
        var coffee = new CoffeeProduct(roaster.Id, new("Colombia test", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, [], [], [], []));
        coffee.SetStatus(CoffeePublicationStatus.Published);
        var coffeeSource = new CoffeeProductSource { CoffeeProductId = coffee.Id, SourceKey = "roast.by", ExternalProductId = "coffee",
            SourceUrl = "https://roast.by/coffee/", SellerName = "Test seller", LastSeenAtUtc = DateTime.UtcNow };
        coffeeSource.Variants.Add(new CoffeeVariant { SourceKey = "roast.by", ExternalVariantId = "bag-250", WeightGrams = 250,
            Price = 30, Currency = "BYN", Availability = CoffeeAvailability.InStock, CheckedAtUtc = DateTime.UtcNow });
        coffee.Sources.Add(coffeeSource);
        await using (var db = Shops())
        {
            db.AddRange(shop, bean, equipment, method, tag, roaster, coffee);
            db.Entry(equipment.Category).Property(nameof(EquipmentCategory.Name)).CurrentValue = "Espresso machines";
            await db.SaveChangesAsync(Ct);
        }
        var internalIds = new[] { shop.Id, city.Id, bean.Id, equipment.Id, method.Id, tag.Id, roaster.Id, user.Id, coffee.Id };
        var summary = new ShortShopDto
        {
            Id = shop.Id, CityId = city.Id, Name = shop.Name, Photos = [],
            Beans = [new CoffeeBeansDto { Id = bean.Id, Name = bean.Name }],
            Roasters = [new RoasterDto { Id = roaster.Id, Name = roaster.Name }],
            Equipments = [new EquipmentDto { Id = equipment.Id, Name = equipment.Name, Brand = equipment.Brand, Model = equipment.ModelName }],
            BrewMethods = [new BrewMethodDto { Id = method.Id, Name = method.Name }]
        };
        var visit = new UnifiedCheckInDto
        {
            Id = Guid.NewGuid(), UserId = user.Id, ShopId = shop.Id, Username = "Petr", ShopName = shop.Name,
            Text = "Lovely coffee", Rating = new RatingDto { Place = 5, Coffee = 5, Service = 5 },
            Drink = new CheckInDrinkDto("other", "Эспрессо-тоник", "Эспрессо-тоник", "Espresso tonic"),
            Visibility = CheckInVisibility.Public, ModerationState = CheckInModerationState.Approved,
            VisitedAt = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow, ContentRevision = 1
        };
        var detail = new CoffeeShopDetailsDto { Id = shop.Id, CityId = city.Id, Name = shop.Name, Photos = [],
            CoffeeBeans = summary.Beans, Roasters = summary.Roasters, Equipments = summary.Equipments,
            BrewMethods = summary.BrewMethods, CheckIns = [visit], Tags = [new ShopTagDto(tag.Id, tag.Slug, tag.Name, null, 0)] };
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
        bus.Setup(x => x.InvokeAsync<Response<RoasterDetailsDto>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<RoasterDetailsDto>.Success(new() { Id = roaster.Id, Name = roaster.Name,
                Shops = [new() { Id = shop.Id, Name = shop.Name }] }));
        bus.Setup(x => x.InvokeAsync<Response<GetShopsInBoundsResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetShopsInBoundsResponse>.Success(new([new() { Id = shop.Id, Title = shop.Name, Latitude = 53.9m, Longitude = 27.56m }])));
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
        CreateUnifiedCheckInCommand? capturedCheckIn = null;
        bus.Setup(x => x.InvokeAsync<Response<UnifiedCheckInDto>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((object command, CancellationToken _, TimeSpan? _) =>
            {
                capturedCheckIn = (CreateUnifiedCheckInCommand)command;
                return Response<UnifiedCheckInDto>.Success(visit with
                {
                    Text = capturedCheckIn.Request.Text, Rating = capturedCheckIn.Request.Rating,
                    Visibility = capturedCheckIn.Request.Visibility,
                    ModerationState = capturedCheckIn.Request.Visibility == CheckInVisibility.Public
                        ? CheckInModerationState.Pending : CheckInModerationState.NotSubmitted
                });
            });
        var checkInQueries = new Mock<ICheckInQueries>();
        checkInQueries.Setup(x => x.GetUnifiedByUserAsync(user.Id, 1, 10, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckInPage([visit with { Visibility = CheckInVisibility.Private,
                ModerationState = CheckInModerationState.NotSubmitted }], 1));
        checkInQueries.Setup(x => x.GetPublicAsync(It.IsAny<Guid?>(), null, shop.Id, user.Id, 20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicCheckInPage([visit], null));
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
        shopsBuilder.Services.AddSingleton(checkInQueries.Object);
        shopsBuilder.Services.AddSingleton(Mock.Of<IQueryCheckInRepository>());
        shopsBuilder.Services.AddScoped<IProfileAddressLookup, CoffeePeek.Shops.Infrastructure.Account.AccountProfileAddressLookup>();
        shopsBuilder.Services.AddScoped<PublicAddressHttpClient>();
        shopsBuilder.Services.AddHttpClient("account-user-lookup", c => c.BaseAddress = new Uri(Address(account)));
        shopsBuilder.Services.AddScoped<PublicDiscoveryService>();
        shopsBuilder.Services.AddScoped<IPublicCheckInProjector, PublicCheckInProjector>();
        shopsBuilder.Services.AddScoped<ICoffeeCatalogStore, CoffeeCatalogStore>();
        shopsBuilder.Services.AddOptions<CoffeeImportOptions>();
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

        // The literal by-slug route must win over the generic profile route.
        (await Get("/api/Users/by-slug/reviews", HttpStatusCode.ServiceUnavailable, "account"))!["errorCode"]
            .Should().NotBeNull();
        await Get($"/api/Users/{user.Id}", HttpStatusCode.ServiceUnavailable, "account");
        foreach (var retired in new[] { "/api/users/petr/reviews", "/api/CheckIns", "/api/v2/check-ins" })
        {
            using var removed = await http.GetAsync(retired, Ct);
            removed.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
        foreach (var catalog in new[] { "cities", "beans", "equipments", "brew-methods" })
        {
            var catalogJson = await Get("/api/Catalogs/" + catalog, HttpStatusCode.OK, "shops");
            NoInternalIds(catalogJson);
            catalogJson!["data"]!.AsArray().Should().HaveCount(1);
        }
        NoInternalIds(await Get("/api/Roasters/beans-roastery", HttpStatusCode.OK, "shops"));
        var roasters = await Get("/api/roasters", HttpStatusCode.OK, "shops");
        NoInternalIds(roasters);
        var publicRoaster = roasters!["data"]!.AsArray().Should().ContainSingle().Which!;
        publicRoaster["address"]!["slug"]!.GetValue<string>().Should().Be("beans-roastery");
        publicRoaster["coffeeProductsCount"]!.GetValue<int>().Should().Be(1);
        publicRoaster["availableCoffeeProducts"]!.GetValue<int>().Should().Be(1);
        foreach (var route in new[] { "/api/Catalogs/roasters", "/api/v1/roasters/search", "/api/v1/roasters/facets" })
        {
            using var removed = route.StartsWith("/api/v1/")
                ? await http.PostAsJsonAsync(route, new { }, Ct) : await http.GetAsync(route, Ct);
            removed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        NoInternalIds(await Get("/api/roasters/beans-roastery/coffees", HttpStatusCode.OK, "shops"));
        var coffees = await Get("/api/coffees?weightGrams=250&currency=BYN&maxPrice=30", HttpStatusCode.OK, "shops");
        NoInternalIds(coffees);
        coffees!["data"]!["items"]!.AsArray().Should().HaveCount(1);
        NoInternalIds(await Get("/api/coffees/colombia-test", HttpStatusCode.OK, "shops"));
        NoInternalIds(await Get("/api/Catalogs/coffee-origin-countries", HttpStatusCode.OK, "shops"));
        NoInternalIds(await Get("/api/Catalogs/roaster-tags", HttpStatusCode.OK, "shops"));
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
        await Get("/api/Users/by-slug/IVAN-PETROV", HttpStatusCode.NotFound, "account");
        var canonical = await Get("/api/Users/by-slug/petr", HttpStatusCode.OK, "account");
        canonical!["address"]!["isAlias"]!.GetValue<bool>().Should().BeFalse();
        canonical["address"]!["canonicalPath"]!.GetValue<string>().Should().Be("/users/petr");
        canonical["address"]!["revision"]!.GetValue<long>().Should().Be(2);
        canonical["data"]!["userName"]!.GetValue<string>().Should().Be("Petr");
        canonical["data"]!.AsObject().ContainsKey("email").Should().BeFalse();
        // Browsing more than 20 profiles must not consume the login/token-refresh bucket.
        for (var i = 0; i < 21; i++) await Get("/api/Users/by-slug/petr", HttpStatusCode.OK, "account");
        NoInternalIds(canonical);
        var shopDetail = await Get("/api/CoffeeShops/by-slug/26-october-16", HttpStatusCode.OK, "shops");
        NoInternalIds(shopDetail);
        shopDetail!["data"]!["checkIns"]![0]!["author"]!["slug"]!.GetValue<string>().Should().Be("petr");
        shopDetail["data"]!["checkIns"]![0]!["id"]!.GetValue<Guid>().Should().Be(visit.Id);
        shopDetail["data"]!["checkIns"]![0]!["drinkSlug"]!.GetValue<string>().Should().Be("other");
        shopDetail["data"]!["checkIns"]![0]!["customDrinkName"]!.GetValue<string>().Should().Be("Эспрессо-тоник");
        shopDetail["data"]!["checkIns"]![0]!["drinkNameRu"]!.GetValue<string>().Should().Be("Эспрессо-тоник");
        shopDetail["data"]!["checkIns"]![0]!["drinkNameEn"]!.GetValue<string>().Should().Be("Espresso tonic");
        using (var authenticated = new HttpClient { BaseAddress = new Uri(Address(shops)) })
        {
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "http-smoke-internal-secret-at-least-32-characters");
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, user.Id.ToString());
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserName, "Petr");
            foreach (var incomplete in new object[]
            {
                new { text = "Great coffee", rating = new { place = 5, coffee = 5, service = 5 } },
                new { coffeeShopSlug = "26-october-16", rating = new { place = 5, coffee = 5, service = 5 } },
                new { coffeeShopSlug = "26-october-16", text = "Great coffee" }
            })
            {
                using var rejected = await authenticated.PostAsJsonAsync("/api/v1/check-ins", incomplete, Ct);
                rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }
            capturedCheckIn.Should().BeNull("MVC must reject missing required fields before dispatching creation");
            foreach (var visibility in new[] { "Private", "Public" })
            {
                using var created = await authenticated.PostAsJsonAsync("/api/v1/check-ins", new
                {
                    coffeeShopSlug = "26-october-16", text = "Great coffee", visibility, visitedAt = DateTime.UtcNow,
                    drinkSlug = "other", customDrinkName = "Эспрессо-тоник", rating = new { place = 5, coffee = 5, service = 5 },
                    userId = Guid.NewGuid(), username = "spoof"
                }, Ct);
                created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Ct));
                capturedCheckIn!.Request.CoffeeShopId.Should().Be(shop.Id);
                capturedCheckIn.UserId.Should().Be(user.Id);
                capturedCheckIn.Username.Should().Be("Petr");
                capturedCheckIn.Request.Text.Should().Be("Great coffee");
                capturedCheckIn.Request.DrinkSlug.Should().Be("other");
                capturedCheckIn.Request.CustomDrinkName.Should().Be("Эспрессо-тоник");
                var createdJson = JsonNode.Parse(await created.Content.ReadAsStringAsync(Ct));
                NoInternalIds(createdJson);
                createdJson!["data"]!["id"]!.GetValue<Guid>().Should().Be(visit.Id);
                createdJson["data"]!["shop"]!["slug"]!.GetValue<string>().Should().Be("26-october-16");
                createdJson["data"]!["author"]!["slug"]!.GetValue<string>().Should().Be("petr");
                createdJson["data"]!["visibility"]!.GetValue<string>().Should().Be(visibility);
                createdJson["data"]!["moderationState"]!.GetValue<string>()
                    .Should().Be(visibility == "Public" ? "Pending" : "NotSubmitted");
            }
            using var checkInsResponse = await authenticated.GetAsync("/api/v1/check-ins/mine", Ct);
            checkInsResponse.StatusCode.Should().Be(HttpStatusCode.OK, await checkInsResponse.Content.ReadAsStringAsync(Ct));
            var checkIns = JsonNode.Parse(await checkInsResponse.Content.ReadAsStringAsync(Ct));
            NoInternalIds(checkIns);
            var checkIn = checkIns!["data"]!["items"]![0]!;
            checkIn["drinkSlug"]!.GetValue<string>().Should().Be("other");
            checkIn["customDrinkName"]!.GetValue<string>().Should().Be("Эспрессо-тоник");
            checkIn["drinkNameRu"]!.GetValue<string>().Should().Be("Эспрессо-тоник");
            checkIn["drinkNameEn"]!.GetValue<string>().Should().Be("Espresso tonic");
            using var invalid = await authenticated.PostAsJsonAsync("/api/v1/check-ins", new
            { coffeeShopSlug = shop.Id.ToString(), text = "Great coffee", rating = new { place = 5, coffee = 5, service = 5 } }, Ct);
            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        var publicFeed = await Get("/api/v1/check-ins?coffeeShopSlug=26-october-16&authorSlug=petr", HttpStatusCode.OK, "shops");
        NoInternalIds(publicFeed);
        publicFeed!["data"]!["items"]![0]!["author"]!["slug"]!.GetValue<string>().Should().Be("petr");
        publicFeed["data"]!["items"]![0]!["moderationState"]!.GetValue<string>().Should().Be("Approved");
        using (var authenticated = new HttpClient { BaseAddress = new Uri(Address(moderation)) })
        {
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "http-smoke-internal-secret-at-least-32-characters");
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserId, user.Id.ToString());
            authenticated.DefaultRequestHeaders.Add(GatewayHeaderConsts.XUserName, "Petr");
            using var retiredReviewSubmission = await authenticated.PostAsJsonAsync("/api/ModerationReviews", new { }, Ct);
            retiredReviewSubmission.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
