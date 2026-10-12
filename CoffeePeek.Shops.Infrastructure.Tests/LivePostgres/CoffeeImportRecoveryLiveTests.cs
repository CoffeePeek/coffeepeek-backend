using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;
using CoffeePeek.Shops.Infrastructure.Tests.Persistence;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CoffeeImportRecoveryLiveTests(ShopsLiveDatabase fixture) : IClassFixture<ShopsLiveDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSnapshots_InBothCommitOrders_KeepNewestOffersAndManualFields(bool newestFirst)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var (source, roaster, product, tag, initial) = await SeedAsync();
        var old = Snapshot(source, initial.AddMinutes(1), 31);
        var newer = Snapshot(source, initial.AddMinutes(2), 32);
        await using var firstDb = fixture.Context(false);
        await using var secondDb = fixture.Context(false);
        await using var transaction = await firstDb.Database.BeginTransactionAsync(Ct);
        await firstDb.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7402100101)", Ct);
        var second = Service(secondDb, source, roaster).ImportAsync(newestFirst ? old : newer, Ct);
        await LivePostgresServer.WaitForAdvisoryWaitersAsync(fixture.ConnectionString, 7402100101, 1, Ct);
        Assert.Equal("Applied", (await Service(firstDb, source, roaster).ImportAsync(newestFirst ? newer : old, Ct)).Status);
        await transaction.CommitAsync(Ct);
        if (newestFirst)
            Assert.Equal(409, (await Assert.ThrowsAsync<CoffeeCatalogException>(() => second)).StatusCode);
        else Assert.Equal("Applied", (await second).Status);
        await AssertStateAsync(source, product, roaster, tag, 32);
        await using var replay = fixture.Context(false);
        var writer = Service(replay, source, roaster);
        var applied = await writer.ImportAsync(newer, Ct);
        Assert.Equal(applied.RunId, (await writer.ImportAsync(newer, Ct)).RunId);
        Assert.Equal(newestFirst ? 2 : 3, await replay.CoffeeImportRuns.CountAsync(r => r.SourceKey == source && r.Status == "Applied", Ct));
    }

    [Fact]
    public async Task FailureAfterRealSqlFlush_RollsBackWholeSnapshot_AndSamePayloadResumesWithoutDuplicates()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var (source, roaster, product, tag, initial) = await SeedAsync();
        var request = Snapshot(source, initial.AddMinutes(1), 31);
        request.Snapshot.Products[0].Variants = [.. request.Snapshot.Products[0].Variants,
            new() { ExternalVariantId = "v3", WeightGrams = 500, Price = 50, Currency = "BYN", AvailableForPurchase = true }];
        var probe = new ImportAfterFlushProbe(async db =>
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Equal(3, await db.Set<CoffeeVariant>().CountAsync(v => v.Source.CoffeeProductId == product, Ct));
            await AssertStateAsync(source, product, roaster, tag, 30);
            throw new InvalidOperationException("Injected failure after import SQL flush.");
        });
        await using var failing = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options);
        var service = Service(failing, source, roaster);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(request, Ct));
        Assert.All(failing.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        await AssertStateAsync(source, product, roaster, tag, 30);
        await using (var verify = fixture.Context(false))
        {
            var run = await verify.CoffeeImportRuns.SingleAsync(r => r.SourceKey == source && r.SnapshotId == request.SnapshotId, Ct);
            Assert.Equal("Failed", run.Status);
            Assert.NotEqual(request.SnapshotId, run.PayloadHash);
        }
        var resumed = await service.ImportAsync(request, Ct);
        Assert.Equal("Applied", resumed.Status);
        Assert.Equal(resumed.RunId, (await service.ImportAsync(request, Ct)).RunId);
        await AssertStateAsync(source, product, roaster, tag, 31, 3);
        await using var final = fixture.Context(false);
        Assert.Equal(2, await final.CoffeeImportRuns.CountAsync(r => r.SourceKey == source, Ct));
        request.Snapshot.Products[0].Name = "Changed payload";
        Assert.Equal(409, (await Assert.ThrowsAsync<CoffeeCatalogException>(() => service.ImportAsync(request, Ct))).StatusCode);
        await AssertStateAsync(source, product, roaster, tag, 31, 3);
    }

    private async Task<(string Source, Guid Roaster, Guid Product, Guid Tag, DateTimeOffset Date)> SeedAsync()
    {
        var source = "import-" + Guid.NewGuid().ToString("N") + ".example";
        var date = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using var db = fixture.Context(false);
        var roaster = new Roaster(source); roaster.SetPublicSlug("import-" + Guid.NewGuid().ToString("N"));
        var tag = new RoasterTag("manual-" + Guid.NewGuid().ToString("N"), "Manual tag", null, 0);
        db.Roasters.Add(roaster); db.RoasterTags.Add(tag);
        db.RoasterTagAssignments.Add(new(roaster.Id, tag.Id, Guid.NewGuid()));
        await db.SaveChangesAsync(Ct);
        await Service(db, source, roaster.Id).ImportAsync(Snapshot(source, date, 30), Ct);
        var product = await db.CoffeeProducts.SingleAsync(p => p.RoasterId == roaster.Id, Ct);
        product.ApplyContent(new("Manual name", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, [], [], [], []));
        product.ProtectFields(["Name", "Countries"]);
        product.ReplaceCountries(["CO"]);
        product.ApplyClassification(new([], null, null, null, [], [], ["chocolate"], null));
        product.ProtectClassificationFields(["tasteGroups"]);
        product.SetStatus(CoffeePublicationStatus.Published);
        await db.SaveChangesAsync(Ct);
        return (source, roaster.Id, product.Id, tag.Id, date);
    }

    private static CoffeeSnapshotImportRequest Snapshot(string source, DateTimeOffset date, decimal price)
    {
        var request = CoffeeCatalogTests.Snapshot('a', date);
        request.SourceKey = source; request.Snapshot.Source = "https://" + source + "/";
        request.SnapshotId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        var item = request.Snapshot.Products[0];
        item.SourceKey = source; item.SourceUrl = request.Snapshot.Source;
        item.ImageUrls = []; item.StoredPhotos = []; item.Name = "Imported name";
        item.Origins[0].CountryCode = "ET"; item.Variants[0].Price = price;
        return request;
    }

    private static CoffeeImportService Service(ShopsDbContext db, string source, Guid roaster) =>
        new(new CoffeeImportStorage(db), Options.Create(new CoffeeImportOptions
            { Roasters = new() { [source] = new() { ["ROAST"] = roaster } } }), NullLogger.Instance);

    private async Task AssertStateAsync(string source, Guid id, Guid roaster, Guid tag, decimal price, int variants = 2)
    {
        await using var db = fixture.Context(false);
        var product = await db.CoffeeProducts.Include(p => p.Countries).Include(p => p.Sources).ThenInclude(s => s.Variants)
            .SingleAsync(p => p.RoasterId == roaster, Ct);
        Assert.Equal(id, product.Id); Assert.Equal("Manual name", product.Name);
        Assert.Equal(CoffeePublicationStatus.Published, product.Status);
        Assert.Equal("CO", Assert.Single(product.Countries).CountryCode);
        Assert.Equal(["chocolate"], product.TasteGroupCodes);
        var imported = Assert.Single(product.Sources);
        Assert.Equal(source, imported.SourceKey); Assert.Equal(variants, imported.Variants.Count);
        Assert.Equal(price, imported.Variants.Single(v => v.ExternalVariantId == "v1").Price);
        var assignment = await db.RoasterTagAssignments.SingleAsync(a => a.RoasterId == roaster, Ct);
        Assert.Equal(tag, assignment.TagId);
    }
}
