using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Infrastructure.Tests.Faults;
using CoffeePeek.Shops.Infrastructure.Tests.Persistence;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Shops.Persistance.Modules;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Wolverine;
using Wolverine.Persistence.Durability;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopsWritePerformanceLiveTests(ShopsLiveDatabase fixture) : IClassFixture<ShopsLiveDatabase>
{
    [Fact]
    public async Task WriteExperiment_RecordsProfileImportAndStoredOcrTransactions_AndDrainsDurableQueue()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Write performance");
        var roaster = new Roaster("Write performance");
        var shops = Enumerable.Range(0, 64).Select(i => ShopsLiveDatabase.NewShop("Write " + i, city.Id)).ToArray();
        var jobs = new List<MenuOcrJob>();
        await using (var seed = fixture.Context(false))
        {
            seed.Cities.Add(city); seed.Roasters.Add(roaster); seed.Shops.AddRange(shops);
            foreach (var shop in shops.Take(16))
            {
                var menu = ShopMenu.Create(shop.Id);
                var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "performance/" + shop.Id, 3);
                menu.AddPhotos([photo]);
                var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
                job.Start(DateTime.UtcNow);
                job.StoreResult(JsonSerializer.Serialize(new ParseMenuPhotosResponse(true, null, null,
                    [new ParsedMenuItemDto("espresso", 8, "espresso")], []), new JsonSerializerOptions(JsonSerializerDefaults.Web)), DateTime.UtcNow);
                jobs.Add(job); seed.ShopMenus.Add(menu);
            }
            await seed.SaveChangesAsync(ct);
        }
        var transactions = new WriteTransactionProbe();
        var poolConnection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            { ApplicationName = "cp-write-experiment", MaxPoolSize = 4 }.ConnectionString;
        using var host = new ShopsDirectWriteBoundaryLiveTests(fixture).CreateHost(services =>
        {
            services.AddScoped(_ => new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(poolConnection).AddInterceptors(new CheckInAuditInterceptor(), transactions).Options));
            services.AddShopsModule().AddCoffeeCatalogModule().AddMenuModule();
            services.AddSingleton(Mock.Of<IAddressGeocoder>());
            services.AddScoped<IUnitOfWork, UnitOfWork<ShopsDbContext>>();
            services.AddScoped<IShopPriceRangeWriter, ShopPriceRangeWriter>();
            services.AddSingleton(Mock.Of<IStrictCacheInvalidator>());
            services.AddSingleton(Mock.Of<IPublicAddressService>());
            services.AddSingleton(Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }));
            services.AddSingleton(Options.Create(new CoffeeImportOptions
                { Roasters = new() { ["roast.by"] = new() { ["ROAST"] = roaster.Id } } }));
        }, typeof(UpdateAdminCoffeeShopHandler), typeof(CoffeeCatalogHandler), typeof(ShopProfileCacheInvalidationRequestedHandler));
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var results = new List<object>();
            await MeasureAsync("profile", 64, 4, async (i, token) =>
                Assert.True((await bus.InvokeAsync<Response<AdminPublishedShopDto>>(new UpdateAdminCoffeeShopCommand(
                    shops[i].Id, "Updated " + i, null, Contract.Enums.PriceRange.Moderate,
                    null, null, null, null, null) { ExpectedRevision = 0 }, token)).IsSuccess));
            var imports = Enumerable.Range(0, 8).Select(i =>
            {
                var request = CoffeeCatalogTests.Snapshot((char)('0' + i), DateTimeOffset.UtcNow.AddMinutes(-10 + i));
                var template = request.Snapshot.Products[0]; template.ImageUrls = []; template.StoredPhotos = [];
                request.Snapshot.Products = Enumerable.Range(0, 100).Select(n =>
                {
                    var product = JsonSerializer.Deserialize<Contract.Dtos.Coffee.CoffeeSnapshotProduct>(JsonSerializer.Serialize(template))!;
                    product.SourceProductId = "performance-" + n; product.SourceUrl += n;
                    foreach (var variant in product.Variants) { variant.ExternalVariantId += "-" + n; variant.Price += i; }
                    return product;
                }).ToArray();
                request.Snapshot.Coverage[0].ExpectedProducts = 100; request.Snapshot.Coverage[0].DiscoveredProducts = 100;
                return request;
            }).ToArray();
            await MeasureAsync("import-100-products", 8, 1, async (i, token) =>
                Assert.Equal("Applied", (await bus.InvokeAsync<CoffeeImportResult>(new ImportCoffeeSnapshotCommand(imports[i]), token)).Status));
            await MeasureAsync("ocr-stored-apply", 16, 4, async (i, token) =>
            {
                await using var scope = host.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>().ApplyStoredAsync(jobs[i].Id, null, token);
            });
            var store = host.Services.GetRequiredService<IMessageStore>();
            await ShopsFaultControl.UntilAsync(async () =>
            {
                var counts = await store.Admin.FetchCountsAsync();
                return counts.Incoming == 0 && counts.Outgoing == 0 && counts.Scheduled == 0;
            }, ct);
            Assert.Equal(0, (await store.Admin.FetchCountsAsync()).DeadLetter);
            await using var verify = fixture.Context(false);
            Assert.Equal(64, await verify.Shops.CountAsync(s => s.Location.CityId == city.Id && s.ProfileRevision == 1, ct));
            Assert.Equal(100, await verify.CoffeeProducts.CountAsync(p => p.RoasterId == roaster.Id, ct));
            Assert.Equal(8, await verify.CoffeeImportRuns.CountAsync(r => r.SourceKey == "roast.by" && r.Status == "Applied", ct));
            Assert.Equal(16, await verify.MenuOcrJobs.CountAsync(j => j.Status == MenuOcrJobStatus.Applied, ct));
            Assert.Equal(16, await verify.Set<ShopMenuItem>().CountAsync(ct));
            var report = JsonSerializer.Serialize(new { Experimental = true, BusinessEfPoolSize = 4,
                SeparateWolverinePoolSize = new NpgsqlConnectionStringBuilder(fixture.ConnectionString).MaxPoolSize,
                Commit = Environment.GetEnvironmentVariable("GITHUB_SHA"), Results = results }, new JsonSerializerOptions { WriteIndented = true });
            var output = Environment.GetEnvironmentVariable("COFFEEPEEK_PERF_RESULTS") ?? Path.Combine(Path.GetTempPath(), "cp-performance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "write-experiment.json"), report, ct);
            Console.WriteLine("WRITE_PERFORMANCE " + report);

            async Task MeasureAsync(string scenario, int samples, int clients, Func<int, CancellationToken, Task> action)
            {
                transactions.CommittedMilliseconds.Clear();
                var latencies = new ConcurrentBag<double>();
                var allocated = GC.GetTotalAllocatedBytes(true);
                await Parallel.ForEachAsync(Enumerable.Range(0, samples), new ParallelOptions
                    { MaxDegreeOfParallelism = clients, CancellationToken = ct }, async (i, token) =>
                {
                    var watch = Stopwatch.StartNew(); await action(i, token); latencies.Add(watch.Elapsed.TotalMilliseconds);
                });
                var ordered = latencies.Order().ToArray();
                var tx = transactions.CommittedMilliseconds.Order().ToArray();
                Assert.NotEmpty(tx);
                results.Add(new { Scenario = scenario, Clients = clients, Samples = samples,
                    P50Ms = Percentile(ordered, .5), P95Ms = Percentile(ordered, .95), P99Ms = Percentile(ordered, .99),
                    CommittedTransactions = tx.Length, TransactionP95Ms = Percentile(tx, .95), TransactionMaxMs = tx[^1],
                    BatchAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64 });
            }
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private static double Percentile(double[] sorted, double quantile) => sorted[(int)Math.Ceiling(quantile * sorted.Length) - 1];
}
