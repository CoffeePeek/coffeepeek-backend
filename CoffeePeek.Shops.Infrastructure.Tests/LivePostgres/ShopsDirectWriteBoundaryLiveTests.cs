using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Shops.Persistance.PublicAddresses;
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

    [Theory]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("edit", false)]
    [InlineData("edit", true)]
    [InlineData("deactivate", false)]
    [InlineData("deactivate", true)]
    [InlineData("product", false)]
    [InlineData("product", true)]
    [InlineData("variant", false)]
    [InlineData("variant", true)]
    public async Task ClassificationWrite_CommitsBeforeResponse_AndLateFailureRollsBack(string operation, bool failAfterFlush)
    {
        var ct = TestContext.Current.CancellationToken;
        var code = "test-" + Guid.NewGuid().ToString("N");
        var value = new CoffeeFilterValue("taste", code, "Original", null, 0);
        var roaster = new Roaster("Classification boundary " + code);
        var product = new CoffeeProduct(roaster.Id, new("Boundary coffee", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, [], [], [], []));
        var source = new CoffeeProductSource { Product = product, CoffeeProductId = product.Id,
            SourceKey = "boundary.example", ExternalProductId = code, SourceUrl = "https://boundary.example/coffee" };
        var variant = new CoffeeVariant { Source = source, CoffeeProductSourceId = source.Id,
            SourceKey = source.SourceKey, ExternalVariantId = code, Currency = "BYN", Price = 10 };
        source.AttachVariant(variant); product.AttachSource(source);
        await using (var seed = fixture.Context(false))
        {
            seed.Roasters.Add(roaster); seed.CoffeeProducts.Add(product);
            if (operation != "create") seed.CoffeeFilterValues.Add(value);
            await seed.SaveChangesAsync(ct);
        }
        var version = product.Version;
        object command = operation switch
        {
            "create" => new CreateCoffeeFilterValueCommand(new() { GroupCode = "taste", Code = code, Name = "Changed" }),
            "edit" => new EditCoffeeFilterValueCommand(value.Id, new() { Name = "Changed", IsActive = true }),
            "deactivate" => new DeactivateCoffeeFilterValueCommand(value.Id),
            "product" => new EditCoffeeClassificationCommand(product.Id, new()
                { Version = version, Classification = new() { Caffeine = "decaf" }, ProtectedFields = ["caffeine"] }),
            "variant" => new EditCoffeeVariantClassificationCommand(product.Id, variant.Id, new()
                { Version = version, BrewPurpose = "espresso", ProtectFromImport = true }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var flushObserved = false;
        var probe = new AfterSaveProbe(async db =>
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            await AssertStateAsync(false);
            flushObserved = true;
            if (failAfterFlush) throw new InvalidOperationException("Failure after classification flush.");
        });
        using var host = CreateHost(services =>
        {
            services.AddScoped(_ => new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options));
            services.AddScoped<IDiscoveryClassificationStore, DiscoveryClassificationStore>();
        }, typeof(CoffeeClassificationHandler));
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            async Task ExecuteAsync()
            {
                if (operation is "product" or "variant") Assert.NotNull(await bus.InvokeAsync<AdminCoffeeClassification>(command, ct));
                else Assert.NotNull(await bus.InvokeAsync<AdminCoffeeFilterValue>(command, ct));
            }
            if (failAfterFlush) await Assert.ThrowsAsync<InvalidOperationException>(ExecuteAsync);
            else await ExecuteAsync();
            Assert.True(flushObserved);
            await AssertStateAsync(!failAfterFlush);
        }
        finally { await host.StopAsync(CancellationToken.None); }

        async Task AssertStateAsync(bool changed)
        {
            await using var read = fixture.Context(false);
            var storedValue = await read.CoffeeFilterValues.SingleOrDefaultAsync(v => v.GroupCode == "taste" && v.Code == code, ct);
            if (operation == "create" && !changed) Assert.Null(storedValue);
            else
            {
                Assert.NotNull(storedValue);
                Assert.Equal(changed && operation is "create" or "edit" ? "Changed" : "Original", storedValue.Name);
                Assert.Equal(!(changed && operation == "deactivate"), storedValue.IsActive);
            }
            var storedProduct = await read.CoffeeProducts.SingleAsync(p => p.Id == product.Id, ct);
            Assert.Equal(changed && operation == "product" ? "decaf" : null, storedProduct.CaffeineCode);
            Assert.Equal(changed && operation == "product" ? ["caffeine"] : Array.Empty<string>(), storedProduct.ClassificationProtectedFields);
            Assert.Equal(version + (changed ? operation == "product" ? 2 : operation == "variant" ? 1 : 0 : 0), storedProduct.Version);
            var storedVariant = await read.Set<CoffeeVariant>().SingleAsync(v => v.Id == variant.Id, ct);
            Assert.Equal(changed && operation == "variant" ? "espresso" : null, storedVariant.BrewPurposeCode);
            Assert.Equal(changed && operation == "variant", storedVariant.BrewPurposeProtected);
        }
    }

    [Theory]
    [InlineData("initialize", false)]
    [InlineData("initialize", true)]
    [InlineData("rename", false)]
    [InlineData("rename", true)]
    [InlineData("backfill", false)]
    [InlineData("backfill", true)]
    [InlineData("dry-run", false)]
    public async Task PublicAddressWrite_UsesOneCommit_AndLateFailureRollsBack(string operation, bool failAfterFlush)
    {
        var ct = TestContext.Current.CancellationToken;
        var roaster = new Roaster("Address " + Guid.NewGuid().ToString("N"));
        if (operation == "rename") roaster.SetPublicSlug("original-" + Guid.NewGuid().ToString("N"));
        var originalSlug = roaster.PublicSlug; var revision = roaster.SlugRevision;
        await using (var seed = fixture.Context(false))
        { seed.Roasters.Add(roaster); await seed.SaveChangesAsync(ct); }
        var slug = operation is "backfill" or "dry-run" ? PublicSlug.Generate(roaster.Name) : "changed-" + Guid.NewGuid().ToString("N");
        var flushObserved = false;
        var probe = new AfterSaveProbe(async db =>
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            await AssertStateAsync(false);
            flushObserved = true;
            if (failAfterFlush) throw new InvalidOperationException("Failure after address flush.");
        });
        using var host = CreateHost(services =>
        {
            services.AddScoped(_ => new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options,
                Options.Create(new PublicAddressOptions { Enabled = true })));
            services.AddScoped<IPublicAddressService, PublicAddressService>();
        }, typeof(PublicAddressHandler));
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            async Task ExecuteAsync()
            {
                if (operation is "backfill" or "dry-run")
                {
                    var result = await bus.InvokeAsync<PublicAddressBackfillResult>(new BackfillPublicAddressesCommand(
                        PublicAddressKind.Roaster, null, 100, operation == "dry-run", Guid.NewGuid()), ct);
                    Assert.Contains(result.Items, i => i.EntityId == roaster.Id && i.Slug == slug);
                }
                else
                {
                    var result = await bus.InvokeAsync<PublicAddressMetadata>(new ChangePublicAddressCommand(
                        PublicAddressKind.Roaster, roaster.Id, new(slug, revision, "Boundary test"), Guid.NewGuid()), ct);
                    Assert.Equal(slug, result.Slug);
                    Assert.Equal(revision + 1, result.Revision);
                }
            }
            if (failAfterFlush) await Assert.ThrowsAsync<InvalidOperationException>(ExecuteAsync);
            else await ExecuteAsync();
            Assert.True(flushObserved);
            await AssertStateAsync(!failAfterFlush && operation != "dry-run");
        }
        finally { await host.StopAsync(CancellationToken.None); }

        async Task AssertStateAsync(bool changed)
        {
            await using var read = fixture.Context(false);
            var stored = await read.Roasters.SingleAsync(r => r.Id == roaster.Id, ct);
            Assert.Equal(changed ? slug : originalSlug, stored.PublicSlug);
            Assert.Equal(revision + (changed ? 1 : 0), stored.SlugRevision);
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
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IDiscoveryClassificationStore));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IPublicAddressService));
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
