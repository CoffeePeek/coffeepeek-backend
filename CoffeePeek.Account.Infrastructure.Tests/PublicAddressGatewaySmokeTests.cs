using System.Net;
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
    public async Task ProductionRoutes_PreserveEnvelopesVisibilityGuidRoutesAndFlagRollback()
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
        await using var shops = shopsBuilder.Build();
        MapControllers(shops, "shops");
        await shops.StartAsync(Ct);

        var gatewayBuilder = Host();
        gatewayBuilder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway-smoke.json"));
        foreach (var cluster in gatewayBuilder.Configuration.GetSection("ReverseProxy:Clusters").GetChildren())
            gatewayBuilder.Configuration[$"ReverseProxy:Clusters:{cluster.Key}:Destinations:destination1:Address"] =
                cluster.Key == "account-cluster" ? Address(account) : Address(shops);
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
        await Get($"/api/Users/{user.Id}", HttpStatusCode.OK, "account");
        using (var reviews = await http.GetAsync($"/api/users/{user.Id}/reviews", Ct))
        {
            // The real reviews controller requires authentication; an anonymous request must reach Shops and get 401.
            reviews.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            reviews.Headers.GetValues("X-Smoke-Service").Should().ContainSingle().Which.Should().Be("shops");
        }
        await Get($"/api/Catalogs/cities/{city.Id}/public-address", HttpStatusCode.ServiceUnavailable, "shops");
        shopsFlags.Enabled = true;
        await Get($"/api/Catalogs/cities/{city.Id}/public-address", HttpStatusCode.NotFound, "shops");
        await using (var db = Shops()) await new PublicAddressService(db).BackfillAsync(
            CoffeePeek.Shops.Application.PublicAddresses.PublicAddressKind.City, null, 100, false, Guid.NewGuid(), Ct);
        var cityEnvelope = await Get("/api/Catalogs/cities/by-slug/MINSK", HttpStatusCode.OK, "shops");
        cityEnvelope!["address"]!["canonicalPath"]!.GetValue<string>().Should().Be("/cities/minsk");
        var cityBatch = await Get($"/api/Catalogs/cities/public-addresses?ids={city.Id}&ids={Guid.NewGuid()}", HttpStatusCode.OK, "shops");
        cityBatch!.AsArray().Should().ContainSingle();

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
        var batch = await Get($"/api/Users/public-addresses?ids={blocked.Id}&ids={user.Id}&ids={deleted.Id}", HttpStatusCode.OK, "account");
        batch!.AsArray().Should().ContainSingle();
        batch[0]!["entityId"]!.GetValue<Guid>().Should().Be(user.Id);
        var legacyAddress = await Get($"/api/Users/{user.Id}/public-address", HttpStatusCode.OK, "account");
        legacyAddress!["canonicalPath"]!.GetValue<string>().Should().Be("/users/petr");
        await Get("/api/Users/public-addresses?ids=invalid", HttpStatusCode.BadRequest, "account");
        var unknown = await Get("/api/Users/by-slug/nobody", HttpStatusCode.NotFound, "account");
        foreach (var slug in new[] { "boris-blocked", "dmitry-deleted" })
        {
            var hidden = await Get("/api/Users/by-slug/" + slug, HttpStatusCode.NotFound, "account");
            hidden!.AsObject().Remove("traceId");
            unknown!.AsObject().Remove("traceId");
            JsonNode.DeepEquals(hidden, unknown).Should().BeTrue();
        }
        await Get($"/api/Users/{blocked.Id}", HttpStatusCode.NotFound, "account");
        accountFlags.Enabled = false;
        shopsFlags.Enabled = false;
        await Get("/api/Users/by-slug/petr", HttpStatusCode.ServiceUnavailable, "account");
        await Get($"/api/Users/public-addresses?ids={user.Id}", HttpStatusCode.ServiceUnavailable, "account");
        await Get($"/api/Users/{user.Id}/public-address", HttpStatusCode.ServiceUnavailable, "account");
        await Get($"/api/Users/{user.Id}", HttpStatusCode.OK, "account");
        await Get("/api/Catalogs/cities/by-slug/minsk", HttpStatusCode.ServiceUnavailable, "shops");

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
