using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

/// <summary>
/// Exercises the actual Wolverine EF transaction middleware against the existing migrated,
/// throwaway PostgreSQL database. A typed failure is followed by a read from a new context:
/// tracked state in the handler cannot be mistaken for committed database state.
/// </summary>
[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopProfileWolverineLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ShopProfileWolverineLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCatalogValidationError_DoesNotCommitEarlierProfileFields(bool owner)
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerUserId = Guid.NewGuid();
        var shop = new CoffeeShop(null, "Original profile", "Original description", PriceRange.Cheap, Guid.NewGuid());
        shop.AssignOwner(ownerUserId);
        var initialCity = new City("Initial city " + Guid.NewGuid().ToString("N"));
        var replacementCity = new City("Replacement city " + Guid.NewGuid().ToString("N"));
        shop.SetLocation(initialCity.Id, "Original address", 53m, 27m);
        shop.SetContact(null, "original@example.test", null, "111");
        shop.ReplaceSchedules([ShopSchedule.Create(DayOfWeek.Monday, false,
            [ShopScheduleInterval.Create(TimeSpan.FromHours(8), TimeSpan.FromHours(18))])]);

        await using (var seed = fixture.Context())
        {
            seed.Cities.AddRange(initialCity, replacementCity);
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(ct);
        }

        var cache = new Mock<ICacheService>(MockBehavior.Strict);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Reuse the fixture's production Npgsql options, audit interceptor and migrations.
                services.AddScoped(_ => fixture.Context());
                services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
                services.AddScoped<IQueryCityRepository, QueryCityRepository>();
                services.AddScoped<IQueryEquipmentRepository, QueryEquipmentRepository>();
                services.AddScoped<IQueryCoffeeBeanRepository, QueryCoffeeBeanRepository>();
                services.AddScoped<IQueryRoasterRepository, QueryRoasterRepository>();
                services.AddScoped<IQueryBrewMethodRepository, QueryBrewMethodRepository>();
                services.AddSingleton(cache.Object);
                services.AddSingleton<IOptions<MediaPublicUrlOptions>>(Options.Create(
                    new MediaPublicUrlOptions
                    {
                        PublicEndpoint = "https://media.example.test",
                        ShopBucketName = "shops"
                    }));
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(ShopProfileWolverineLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(UpdateAdminCoffeeShopHandler));
                options.Discovery.IncludeType(typeof(UpdateOwnerCoffeeShopHandler));
                options.UseRuntimeCompilation();
                options.PersistMessagesWithPostgresql(fixture.ConnectionString);
                options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
                options.UseEntityFrameworkCoreTransactions();
                options.Policies.AutoApplyTransactions();
            })
            .Build();

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var location = new ShopLocationPatch(replacementCity.Id, "Changed address", 54m, 28m);
            var contacts = new ShopContactsPatch("222", "changed@example.test", null, null);
            var schedules = new[] { new ScheduleDto(DayOfWeek.Tuesday, true, []) };
            // This is deliberately the last section: every preceding section is valid.
            var catalogs = new ShopCatalogsPatch([Guid.NewGuid()], null, null, null);

            Func<Task> action = owner
                ? () => bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                    new UpdateOwnerCoffeeShopCommand(shop.Id, ownerUserId, "Changed profile", "Changed description",
                        contacts.PhoneNumber, contacts.Email, contacts.SiteLink, contacts.InstagramLink,
                        location, schedules, catalogs), ct)
                : () => bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                    new UpdateAdminCoffeeShopCommand(shop.Id, "Changed profile", "Changed description",
                        CoffeePeek.Contract.Enums.PriceRange.Expensive, CoffeeShopStatus.TemporarilyClosed,
                        location, contacts, schedules, catalogs), ct);

            await Assert.ThrowsAsync<ValidationException>(action);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }

        await using var verify = fixture.Context();
        var persisted = await verify.Shops.Include(s => s.Schedules).SingleAsync(s => s.Id == shop.Id, ct);
        Assert.Equal("Original profile", persisted.Name);
        Assert.Equal("Original description", persisted.Description);
        Assert.Equal(PriceRange.Cheap, persisted.PriceRange);
        Assert.Equal(CoffeeShopStatus.Active, persisted.Status);
        Assert.Equal(initialCity.Id, persisted.Location.CityId);
        Assert.Equal("Original address", persisted.Location.Address);
        Assert.Equal(53m, persisted.Location.Latitude);
        Assert.Equal(27m, persisted.Location.Longitude);
        Assert.Equal("111", persisted.Contact.PhoneNumber);
        Assert.Equal("original@example.test", persisted.Contact.Email);
        Assert.Equal(DayOfWeek.Monday, Assert.Single(persisted.Schedules).DayOfWeek);
        cache.VerifyNoOtherCalls();
    }
}
