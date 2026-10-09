using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Testing;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopsDirectWriteBoundaryLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ShopsDirectWriteBoundaryLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task FavoriteWrite_CommitsBeforeResponse_AndLateFailureRollsBack(bool favorite, bool failAfterFlush)
    {
        var ct = TestContext.Current.CancellationToken;
        var roaster = new Roaster("Favorite-boundary-" + Guid.NewGuid().ToString("N"));
        roaster.SetPublicSlug("favorite-" + Guid.NewGuid().ToString("N"));
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using (var seed = fixture.Context(false))
        {
            seed.Roasters.Add(roaster);
            seed.DiscoveryFavorites.Add(new(otherUserId, "roaster", roaster.Id));
            if (!favorite) seed.DiscoveryFavorites.Add(new(userId, "roaster", roaster.Id));
            await seed.SaveChangesAsync(ct);
        }

        var flushObserved = false;
        var probe = new AfterSaveProbe(async db =>
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            await AssertFavoriteAsync(!favorite);
            flushObserved = true;
            if (failAfterFlush) throw new InvalidOperationException("Failure after favorite flush.");
        });
        using var host = CreateHost(services =>
        {
            services.AddScoped(_ => new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options));
            services.AddSingleton(Mock.Of<IPublicAddressService>());
            services.AddScoped<IDiscoveryFavoritesStore, DiscoveryFavoritesStore>();
        }, typeof(SetDiscoveryFavoriteHandler));

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var command = new SetDiscoveryFavoriteCommand(userId, "roaster", roaster.PublicSlug!, favorite);
            if (failAfterFlush)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => bus.InvokeAsync(command, ct));
                await AssertFavoriteAsync(!favorite);
            }
            else
            {
                await bus.InvokeAsync(command, ct);
                await AssertFavoriteAsync(favorite);
            }
            Assert.True(flushObserved);
        }
        finally { await host.StopAsync(CancellationToken.None); }

        async Task AssertFavoriteAsync(bool expected)
        {
            await using var read = fixture.Context(false);
            Assert.Equal(expected, await read.DiscoveryFavorites.AnyAsync(f => f.UserId == userId && f.EntityId == roaster.Id, ct));
            Assert.True(await read.DiscoveryFavorites.AnyAsync(f => f.UserId == otherUserId && f.EntityId == roaster.Id, ct));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportFailureCommand_CommitsOneRun_AndLateFailureRollsBack(bool failAfterFlush)
    {
        var ct = TestContext.Current.CancellationToken;
        const string source = "failure.example";
        var mapping = new CoffeeImportOptions { Roasters = new() { [source] = new() { ["Test"] = Guid.NewGuid() } } };
        var command = new RecordCoffeeImportFailureCommand(new(source, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow, "collect", "Source unavailable"));
        var flushObserved = false;
        var probe = new AfterSaveProbe(async db =>
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            await AssertRunCountAsync(0);
            flushObserved = true;
            if (failAfterFlush) throw new InvalidOperationException("Failure after import report flush.");
        });
        using var host = CreateHost(services =>
        {
            services.AddScoped(_ => new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options));
            services.AddScoped<IUnitOfWork, UnitOfWork<ShopsDbContext>>();
            services.AddScoped<CoffeeCatalogStore>(provider => new(
                provider.GetRequiredService<ShopsDbContext>(),
                Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }),
                Options.Create(mapping), Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance));
            services.AddScoped<ICoffeeImportWriter>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
            services.AddScoped<ICoffeeProductWriter>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
            services.AddScoped<ICoffeeCatalogStore>(provider => provider.GetRequiredService<CoffeeCatalogStore>());
        }, typeof(CoffeeCatalogHandler));
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            if (failAfterFlush)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => bus.InvokeAsync(command, ct));
                Assert.True(flushObserved);
                await AssertRunCountAsync(0);
                return;
            }
            await bus.InvokeAsync(command, ct);
            Assert.True(flushObserved);
            await using (var read = fixture.Context(false))
            {
                var run = await read.CoffeeImportRuns.SingleAsync(r => r.SourceKey == source && r.SnapshotId == command.Failure.SnapshotId, ct);
                Assert.Equal("Failed", run.Status);
                Assert.Equal("collect: Source unavailable", run.Error);
            }
            await bus.InvokeAsync(command, ct);
            await AssertRunCountAsync(1);
        }
        finally { await host.StopAsync(CancellationToken.None); }

        async Task AssertRunCountAsync(int expected)
        {
            await using var read = fixture.Context(false);
            Assert.Equal(expected, await read.CoffeeImportRuns.CountAsync(r => r.SourceKey == source && r.SnapshotId == command.Failure.SnapshotId, ct));
        }
    }

    private IHost CreateHost(Action<IServiceCollection> configure, Type handler) => Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddScoped(_ => fixture.Context(false));
            configure(services);
        })
        .UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ShopsDirectWriteBoundaryLiveTests).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(handler);
            options.UseRuntimeCompilation();
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IDiscoveryFavoritesStore));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ICoffeeImportWriter));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ICoffeeProductWriter));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ICoffeeCatalogStore));
            options.PersistMessagesWithPostgresql(fixture.ConnectionString, "wolverine");
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableLocalQueues();
            options.Durability.Mode = DurabilityMode.Solo;
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
        }).Build();

    private sealed class AfterSaveProbe(Func<DbContext, Task> observe) : SaveChangesInterceptor
    {
        private int calls;

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 1) await observe(eventData.Context!);
            return result;
        }
    }
}
