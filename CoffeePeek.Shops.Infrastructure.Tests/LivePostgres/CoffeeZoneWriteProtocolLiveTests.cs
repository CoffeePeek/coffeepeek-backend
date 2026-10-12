using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CoffeeZoneWriteProtocolLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public CoffeeZoneWriteProtocolLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData("create", false)]
    [InlineData("update", false)]
    [InlineData("status", false)]
    [InlineData("primary", false)]
    [InlineData("remove", false)]
    [InlineData("create", true)]
    [InlineData("update", true)]
    [InlineData("status", true)]
    [InlineData("primary", true)]
    [InlineData("remove", true)]
    public async Task ReadbackFlush_CommitsOnlyAfterSuccess_AndRollsBackOnLateFailure(string operation, bool failReadback)
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Zone-write-city-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("Zone-write-shop-" + Guid.NewGuid().ToString("N"), city.Id);
        shop.SetLocation(city.Id, "Zone write address", 53.9m, 27.56m);
        var zone = ShopsLiveDatabase.NewZone(city.Id, "Zone-write-" + Guid.NewGuid().ToString("N"));
        var originalName = zone.Name;
        var writtenZoneId = zone.Id;
        var previousZone = ShopsLiveDatabase.NewZone(city.Id, "Previous-zone-" + Guid.NewGuid().ToString("N"));
        previousZone.Publish();
        await using (var seed = fixture.Context(false))
        {
            seed.AddRange(city, shop, previousZone);
            seed.CoffeeZoneMembershipOverrides.Add(new(previousZone.Id, shop.Id, CoffeeZoneMembershipOverrideKind.Primary));
            if (operation != "create")
            {
                seed.CoffeeZones.Add(zone);
                seed.CoffeeZoneMembershipOverrides.Add(new(zone.Id, shop.Id, CoffeeZoneMembershipOverrideKind.Include));
            }
            await seed.SaveChangesAsync(ct);
        }

        var readbackCount = 0;
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context(false));
                services.AddScoped<IUnitOfWork, UnitOfWork<ShopsDbContext>>();
                services.AddScoped<ICoffeeZoneRepository, CoffeeZoneRepository>();
                services.AddScoped<IQueryCityRepository, QueryCityRepository>();
                services.AddScoped<IQueryCoffeeShopRepository, QueryCoffeeShopRepository>();
                services.AddScoped<ICoffeeZoneQueries>(provider =>
                {
                    var db = provider.GetRequiredService<ShopsDbContext>();
                    return new ReadbackProbe(new CoffeeZoneQueries(db), async () =>
                    {
                        Assert.NotNull(db.Database.CurrentTransaction);
                        // A separate connection must still see the original rows after the handler's flush.
                        await AssertOriginalStateAsync();
                        readbackCount++;
                        if (failReadback) throw new ReadbackException();
                    });
                });
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(CoffeeZoneWriteProtocolLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(CreateCoffeeZoneHandler));
                options.Discovery.IncludeType(typeof(UpdateCoffeeZoneHandler));
                options.Discovery.IncludeType(typeof(SetCoffeeZoneStatusHandler));
                options.Discovery.IncludeType(typeof(SetCoffeeZoneMembershipOverrideHandler));
                options.Discovery.IncludeType(typeof(DeleteCoffeeZoneMembershipOverrideHandler));
                options.UseRuntimeCompilation();
                options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ICoffeeZoneQueries));
                options.PersistMessagesWithPostgresql(fixture.ConnectionString, "wolverine");
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options.Policies.UseDurableInboxOnAllListeners();
                options.Policies.UseDurableLocalQueues();
                options.Durability.Mode = DurabilityMode.Solo;
                options.UseEntityFrameworkCoreTransactions();
                options.Policies.AutoApplyTransactions();
            })
            .Build();

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            if (failReadback)
            {
                await Assert.ThrowsAsync<ReadbackException>(WriteAsync);
                await AssertOriginalStateAsync();
            }
            else
            {
                await WriteAsync();
                await using var read = fixture.Context(false);
                var committed = await read.CoffeeZones.AsNoTracking().SingleAsync(z => z.Id == writtenZoneId, ct);
                Assert.Equal(operation == "update" ? originalName + " updated" : originalName, committed.Name);
                Assert.Equal(operation == "update" ? "#A1B2C3" : CoffeeZone.DefaultColor, committed.Color);
                Assert.Equal(operation == "status" ? CoffeeZoneStatus.Published : CoffeeZoneStatus.Draft, committed.Status);
                var current = await read.CoffeeZoneMembershipOverrides.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.ZoneId == committed.Id && x.ShopId == shop.Id, ct);
                if (operation is "create" or "remove") Assert.Null(current);
                else Assert.Equal(operation == "primary" ? CoffeeZoneMembershipOverrideKind.Primary : CoffeeZoneMembershipOverrideKind.Include, current!.Kind);
                var primary = await read.CoffeeZoneMembershipOverrides.AsNoTracking()
                    .SingleAsync(x => x.ShopId == shop.Id && x.Kind == CoffeeZoneMembershipOverrideKind.Primary, ct);
                Assert.Equal(operation == "primary" ? committed.Id : previousZone.Id, primary.ZoneId);
            }
            Assert.Equal(1, readbackCount);

            async Task WriteAsync()
            {
                if (operation is "primary" or "remove")
                {
                    object command = operation == "primary"
                        ? new SetCoffeeZoneMembershipOverrideCommand(zone.Id, shop.Id, CoffeeZoneMembershipOverrideKind.Primary)
                        : new DeleteCoffeeZoneMembershipOverrideCommand(zone.Id, shop.Id);
                    var response = await bus.InvokeAsync<Response<CoffeeZoneMembershipPreviewDto>>(command, ct);
                    Assert.True(response.IsSuccess);
                    var member = Assert.Single(response.Data.Members);
                    Assert.Equal(shop.Id, member.ShopId);
                    Assert.Equal(operation == "primary", member.IsPrimary);
                    Assert.Equal(operation == "primary" ? CoffeeZoneMembershipOverrideKind.Primary : (CoffeeZoneMembershipOverrideKind?)null, member.OverrideKind);
                }
                else
                {
                    object command = operation switch
                    {
                        "create" => new CreateCoffeeZoneCommand(city.Id, zone.Name, null, zone.Polygon.ToArray()),
                        "update" => new UpdateCoffeeZoneCommand(zone.Id, city.Id, originalName + " updated", null, zone.Polygon.ToArray(), "#A1B2C3"),
                        "status" => new SetCoffeeZoneStatusCommand(zone.Id, CoffeeZoneStatus.Published),
                        _ => throw new ArgumentOutOfRangeException(nameof(operation))
                    };
                    var response = await bus.InvokeAsync<Response<AdminCoffeeZoneDto>>(command, ct);
                    Assert.True(response.IsSuccess);
                    Assert.Equal(1, response.Data.ShopCount);
                    Assert.Equal(operation == "update" ? originalName + " updated" : originalName, response.Data.Name);
                    Assert.Equal(operation == "update" ? "#A1B2C3" : CoffeeZone.DefaultColor, response.Data.Color);
                    Assert.Equal(operation == "status" ? CoffeeZoneStatus.Published : CoffeeZoneStatus.Draft, response.Data.Status);
                    writtenZoneId = response.Data.Id;
                }
            }
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }

        async Task AssertOriginalStateAsync()
        {
            await using var read = fixture.Context(false);
            var original = await read.CoffeeZones.AsNoTracking().SingleOrDefaultAsync(z => z.Id == zone.Id, ct);
            if (operation == "create")
            {
                Assert.Null(original);
                Assert.False(await read.CoffeeZones.AnyAsync(z => z.Name == originalName, ct));
            }
            else
            {
                Assert.Equal(originalName, original!.Name);
                Assert.Equal(CoffeeZone.DefaultColor, original.Color);
                Assert.Equal(CoffeeZoneStatus.Draft, original.Status);
                Assert.Equal(CoffeeZoneMembershipOverrideKind.Include, (await read.CoffeeZoneMembershipOverrides.AsNoTracking()
                    .SingleAsync(x => x.ZoneId == zone.Id && x.ShopId == shop.Id, ct)).Kind);
            }
            var primary = await read.CoffeeZoneMembershipOverrides.AsNoTracking()
                .SingleAsync(x => x.ShopId == shop.Id && x.Kind == CoffeeZoneMembershipOverrideKind.Primary, ct);
            Assert.Equal(previousZone.Id, primary.ZoneId);
        }
    }

    private sealed class ReadbackException : Exception;

    private sealed class ReadbackProbe(ICoffeeZoneQueries queries, Func<Task> observe) : ICoffeeZoneQueries
    {
        public Task<AdminCoffeeZoneDto[]> GetAllAsync(Guid? cityId, CancellationToken ct = default) => queries.GetAllAsync(cityId, ct);
        public Task<CoffeeZoneCandidateDto[]> GenerateCandidatesAsync(Guid cityId, int radiusMeters, int minShops, CancellationToken ct = default) =>
            queries.GenerateCandidatesAsync(cityId, radiusMeters, minShops, ct);

        public async Task<AdminCoffeeZoneDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            var result = await queries.GetByIdAsync(id, ct);
            Assert.NotNull(result);
            await observe();
            return result;
        }

        public async Task<CoffeeZoneMembershipPreviewDto?> PreviewMembershipAsync(Guid id, CancellationToken ct = default)
        {
            var result = await queries.PreviewMembershipAsync(id, ct);
            Assert.NotNull(result);
            await observe();
            return result;
        }
    }
}
