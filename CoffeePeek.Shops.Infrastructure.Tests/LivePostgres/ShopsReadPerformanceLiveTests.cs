using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopsReadPerformanceLiveTests(ShopsLiveDatabase fixture) : IClassFixture<ShopsLiveDatabase>
{
    private const int ShopCount = 20_001;
    private const int ProductCount = 1_000;
    private const int Clients = 4;
    private const int Samples = 32;

    [Fact]
    public async Task ReadExperiment_RecordsPlansLatencyMemory_AndVerifiesLargeMapAndBoundedPageQueries()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var city = new City("Performance city"); city.SetPublicSlug("performance-city");
        var profileId = Guid.Empty;
        await using (var db = fixture.Context(false))
        {
            db.Cities.Add(city); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            for (var i = 0; i < ShopCount; i++)
            {
                var shop = new CoffeeShop(null, "Performance " + i, null,
                    CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Moderate, Guid.NewGuid());
                shop.SetLocation(city.Id, "Synthetic address", 53.9m + i % 10 / 10000m, 27.56m + i % 7 / 10000m);
                shop.SetPublicSlug("performance-shop-" + i);
                shop.AddPhotos([new ShopPhoto("synthetic.jpg", "image/jpeg", "performance/" + i, 10, owner)]);
                if (i < 50) shop.AssignOwner(owner);
                if (i == 0) profileId = shop.Id;
                db.Shops.Add(shop);
                if (i < 500)
                    for (var visit = 0; visit < 2; visit++)
                    {
                        var checkIn = CheckIn.CreateUnified(owner, shop.Id, "synthetic", "Synthetic visit",
                            Rating.Create(3 + visit, 4, 5), DateTime.UtcNow.AddDays(-2 + visit), CheckInVisibility.Public);
                        Assert.True(checkIn.ApplyModerationDecision(1, Guid.NewGuid(), 1,
                            CheckInModerationState.Approved, null, DateTime.UtcNow));
                        db.CheckIns.Add(checkIn);
                    }
                if ((i + 1) % 500 == 0 || i == ShopCount - 1)
                {
                    await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
                }
            }
            for (var i = 0; i < 2; i++)
            {
                var zone = new CoffeeZone(city.Id, "Performance zone " + i, null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));
                zone.Publish(); db.CoffeeZones.Add(zone);
            }
            var roaster = new Roaster("Performance roaster"); roaster.SetPublicSlug("performance-roaster");
            db.Roasters.Add(roaster);
            for (var i = 0; i < 40; i++) db.CoffeeFilterValues.Add(new("taste", "performance-taste-" + i, "Taste " + i, null, i));
            for (var i = 0; i < ProductCount; i++)
            {
                var product = new CoffeeProduct(roaster.Id, new("Performance coffee " + i, null, "roasted_beans", "whole_beans",
                    "unknown", null, null, null, null, null, [], [], [], []));
                product.SetPublicSlug("performance-coffee-" + i);
                product.ApplyClassification(new([], null, null, null, [], [], ["performance-taste-" + i % 40], null));
                var source = new CoffeeProductSource(product, "performance.example", i.ToString(), "https://performance.example/" + i);
                source.AttachVariant(new(source, i + "-small", 250, 10 + i % 10, "BYN", CoffeeAvailability.InStock));
                source.AttachVariant(new(source, i + "-large", 1000, 30 + i % 10, "BYN", CoffeeAvailability.InStock));
                product.AttachSource(source); product.SetStatus(CoffeePublicationStatus.Published); db.CoffeeProducts.Add(product);
            }
            await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlRawAsync("ANALYZE", ct);
        }

        var output = Environment.GetEnvironmentVariable("COFFEEPEEK_PERF_RESULTS")
            ?? Path.Combine(Path.GetTempPath(), "cp-performance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var results = new List<ReadPerformanceResult>();
        foreach (var scenario in new[] { "profile", "admin-1", "admin-50", "owner-50", "discovery-shops-1", "discovery-shops-50",
                     "discovery-shops-personal-50", "discovery-coffees-50", "facets", "map-clusters", "map-zones", "map-markers" })
        {
            using var warmProbe = new ReadPerformanceCommandProbe(capturePlans: true);
            await using (var warm = Context(warmProbe)) await ExecuteAsync(scenario, warm, ct);
            var plans = new List<object>();
            await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync(ct);
                foreach (var command in warmProbe.Commands)
                {
                    command.Connection = connection;
                    var plan = (string)(await command.ExecuteScalarAsync(ct))!;
                    using var json = JsonDocument.Parse(plan);
                    plans.Add(new { Sql = command.CommandText,
                        Parameters = command.Parameters.Cast<NpgsqlParameter>().Select(p => new
                            { p.ParameterName, Type = p.NpgsqlDbType.ToString(), p.Value }).ToArray(),
                        Plan = json.RootElement.Clone() });
                }
            }
            await File.WriteAllTextAsync(Path.Combine(output, scenario + "-plans.json"), JsonSerializer.Serialize(plans), ct);
            var measurements = new ConcurrentBag<(double Ms, int Sql)>();
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            await Parallel.ForEachAsync(Enumerable.Range(0, Samples), new ParallelOptions
                { MaxDegreeOfParallelism = Clients, CancellationToken = ct }, async (_, token) =>
            {
                using var probe = new ReadPerformanceCommandProbe();
                await using var db = Context(probe);
                var stopwatch = Stopwatch.StartNew();
                await ExecuteAsync(scenario, db, token);
                measurements.Add((stopwatch.Elapsed.TotalMilliseconds, probe.Count));
            });
            var sorted = measurements.Select(m => m.Ms).Order().ToArray();
            var result = new ReadPerformanceResult(scenario, Clients, Samples, Percentile(.5), Percentile(.95), Percentile(.99),
                measurements.Min(m => m.Sql), measurements.Max(m => m.Sql),
                GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore, Process.GetCurrentProcess().WorkingSet64);
            Assert.Equal(warmProbe.Count, result.MinSql); Assert.Equal(result.MinSql, result.MaxSql);
            results.Add(result);
            double Percentile(double quantile) => sorted[(int)Math.Ceiling(quantile * sorted.Length) - 1];
        }
        Assert.Equal(results.Single(r => r.Scenario == "admin-1").MaxSql, results.Single(r => r.Scenario == "admin-50").MaxSql);
        Assert.Equal(results.Single(r => r.Scenario == "discovery-shops-1").MaxSql, results.Single(r => r.Scenario == "discovery-shops-50").MaxSql);
        var report = JsonSerializer.Serialize(new { Experimental = true, Shops = ShopCount, Products = ProductCount,
            Offers = ProductCount * 2, Zones = 2, ApprovedVisits = 1000,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Processors = Environment.ProcessorCount, Commit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            Results = results }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(output, "read-experiment.json"), report, ct);
        Console.WriteLine("READ_PERFORMANCE " + JsonSerializer.Serialize(results));

        async Task ExecuteAsync(string scenario, ShopsDbContext db, CancellationToken token)
        {
            if (scenario == "profile")
            {
                var profile = await new CoffeeShopRepository(db).GetByIdWithCatalogsAsync(profileId, token);
                Assert.NotNull(profile); Assert.Single(profile.ShopPhotos); Assert.Equal(owner, profile.OwnerUserId);
                return;
            }
            var admin = new AdminCoffeeShopQueryRepository(db);
            if (scenario.StartsWith("admin-", StringComparison.Ordinal))
            {
                var pageSize = scenario == "admin-1" ? 1 : 50;
                var page = await admin.GetPagedAsync(1, pageSize, null, null, token);
                Assert.Equal(ShopCount, page.TotalCount); Assert.Equal(pageSize, page.Items.Count);
                Assert.All(page.Items, s => Assert.Single(s.ShopPhotos)); return;
            }
            if (scenario == "owner-50")
            {
                var page = await admin.GetByOwnerUserIdAsync(owner, token);
                Assert.Equal(50, page.Count); Assert.All(page, s => Assert.Single(s.ShopPhotos)); return;
            }
            var store = new DiscoveryQueryStore(db, Mock.Of<IPublicAddressService>(),
                Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }));
            if (scenario.StartsWith("discovery-shops", StringComparison.Ordinal))
            {
                var pageSize = scenario == "discovery-shops-1" ? 1 : 50;
                var page = await store.SearchShopsAsync(new CoffeeShopSearchRequest { PageSize = pageSize },
                    scenario == "discovery-shops-personal-50" ? owner : null, token);
                Assert.Equal(ShopCount, page.TotalItems); Assert.Equal(pageSize, page.Items.Length); return;
            }
            if (scenario == "discovery-coffees-50")
            {
                var page = await store.SearchCoffeesAsync(new() { PageSize = 50 }, token);
                Assert.Equal(ProductCount, page.TotalItems); Assert.Equal(50, page.Items.Length); return;
            }
            if (scenario == "facets")
            {
                var facets = await store.CoffeeFacetsAsync(new() { Filters = new() { Currency = "BYN", WeightGrams = 250 } }, token);
                Assert.Equal(ProductCount, facets.TotalItems);
                var tastes = facets.Groups.Single(g => g.Code == "taste").Options.Where(o => o.Code.StartsWith("performance-", StringComparison.Ordinal)).ToArray();
                Assert.Equal(40, tastes.Length); Assert.All(tastes, t => Assert.Equal(25, t.Count)); return;
            }
            var zoom = scenario == "map-clusters" ? 5 : scenario == "map-zones" ? 12 : 15;
            var queries = new CoffeeShopQueries(db, Mock.Of<IMapper>(), Mock.Of<IQueryCoffeeDrinkRepository>(),
                Mock.Of<IQueryShopMenuRepository>(), Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }));
            var map = await queries.GetMap(new(53m, 27m, 54m, 28m, zoom), new MapClusteringOptions { MaxResponseItems = 100 }, token);
            Assert.True(map.IsTruncated);
            if (zoom == 5) Assert.Equal(20_000, Assert.Single(map.Clusters!).Count);
            else if (zoom == 12) { Assert.Equal(2, map.Zones!.Length); Assert.All(map.Zones, z => Assert.Equal(ShopCount, z.ShopCount)); }
            else { Assert.Equal(100, map.Shops.Length); Assert.All(map.Shops, s => Assert.NotNull(s.PrimaryZoneId)); }
        }
    }

    private ShopsDbContext Context(ReadPerformanceCommandProbe probe) => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options);
}
