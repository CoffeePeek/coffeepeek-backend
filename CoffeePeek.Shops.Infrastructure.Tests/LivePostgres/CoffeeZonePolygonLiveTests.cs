using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CoffeeZonePolygonLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase _fixture;

    public CoffeeZonePolygonLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        _fixture = fixture;
    }

    [Fact]
    public async Task Polygon_RoundTripsThroughJsonAndDetachedViewCannotChangePersistedGeometry()
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Zone polygon persistence city");
        var original = CoffeeZonePoint.Circle(53.9m, 27.56m, 400);
        var replacement = CoffeeZonePoint.Circle(53.91m, 27.57m, 500);
        var zone = new CoffeeZone(city.Id, "Zone polygon persistence", null, original);

        await using (var db = _fixture.Context(false))
        {
            Assert.False(db.Database.HasPendingModelChanges());
            db.Cities.Add(city);
            db.CoffeeZones.Add(zone);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _fixture.Context(false))
        {
            var loaded = await db.CoffeeZones.SingleAsync(z => z.Id == zone.Id, ct);
            Assert.Equal(original, loaded.Polygon);
            loaded.Update(city.Id, loaded.Name, loaded.Description, replacement);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _fixture.Context(false))
        {
            var loaded = await db.CoffeeZones.SingleAsync(z => z.Id == zone.Id, ct);
            Assert.Equal(replacement, loaded.Polygon);
            Assert.True(loaded.Contains(53.91m, 27.57m));
            loaded.Polygon.Clear();
            db.ChangeTracker.DetectChanges();
            Assert.Equal(EntityState.Unchanged, db.Entry(loaded).State);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _fixture.Context(false))
        {
            var loaded = await db.CoffeeZones.SingleAsync(z => z.Id == zone.Id, ct);
            Assert.Equal(replacement, loaded.Polygon);
        }
    }
}
