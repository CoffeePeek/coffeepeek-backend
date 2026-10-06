using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class DiscoveryClassificationLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public DiscoveryClassificationLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Migration_PersistsQueryableClassifications_AndConcurrentAdminEditReturnsConflict()
    {
        Guid id; long version;
        await using (var db = fixture.Context(false))
        {
            Assert.False(db.Database.HasPendingModelChanges());
            var roaster = new Roaster("Classification live roaster"); db.Roasters.Add(roaster);
            var product = new CoffeeProduct(roaster.Id, new("Live coffee", null, "roasted_beans", "whole_beans", "unknown",
                null, null, null, null, null, [], [], [], []));
            product.ApplyClassification(new(["filter"], "decaf", null, null, ["natural"], ["anaerobic"], ["berry"], null));
            db.CoffeeProducts.Add(product); await db.SaveChangesAsync(Ct);
            id = product.Id; version = product.Version;
        }
        await using var first = fixture.Context(false);
        await using var second = fixture.Context(false);
        await first.CoffeeProducts.SingleAsync(p => p.Id == id, Ct);
        await second.CoffeeProducts.SingleAsync(p => p.Id == id, Ct);
        var request = new CoffeeClassificationEditRequest
        {
            Version = version, ProtectedFields = ["acidity"],
            Classification = new() { DefaultBrewPurposes = ["filter"], Caffeine = "decaf", Acidity = "bright",
                Processing = ["natural"], Fermentation = ["anaerobic"], TasteGroups = ["berry"] }
        };
        var updated = (await new DiscoveryClassificationStore(first).EditClassificationAsync(id, request, Ct))!;
        var conflict = await Assert.ThrowsAsync<DiscoveryException>(() => new DiscoveryClassificationStore(second)
            .EditClassificationAsync(id, request, Ct));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("VERSION_CONFLICT", conflict.ErrorCode);
        await using var verify = fixture.Context(false);
        var persisted = await verify.CoffeeProducts.SingleAsync(p => p.Id == id && p.DefaultBrewPurposes.Contains("filter") &&
            p.ProcessingCodes.Contains("natural") && p.FermentationCodes.Contains("anaerobic") && p.TasteGroupCodes.Contains("berry"), Ct);
        Assert.Equal(updated.Version, persisted.Version);
        Assert.Equal("bright", persisted.AcidityCode);
        Assert.Equal(["acidity"], persisted.ClassificationProtectedFields);
    }
}
