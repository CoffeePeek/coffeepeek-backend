using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CityReferenceLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase _fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CityReferenceLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        _fixture = fixture;
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("roaster")]
    [InlineData("zone")]
    public async Task ReferencedCityCannotBeDeleted(string reference)
    {
        var city = new City("Ref city " + Guid.NewGuid());
        await using (var db = _fixture.Context(false))
        {
            Assert.False(db.Database.HasPendingModelChanges());
            db.Cities.Add(city);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.Context(false))
        {
            AddReference(db, city.Id, reference);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.Context(false))
        {
            var result = await new CityRepository(db).DeleteIfUnusedAsync(city.Id, Ct);
            Assert.Equal(CityDeleteResult.Referenced, result);
            Assert.True(await db.Cities.AnyAsync(c => c.Id == city.Id, Ct));
        }
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("roaster")]
    [InlineData("zone")]
    public async Task NewReferenceToDeletedCityIsRejectedByForeignKey(string reference)
    {
        var city = new City("Deleted city " + Guid.NewGuid());
        await using (var db = _fixture.Context(false))
        {
            db.Cities.Add(city);
            await db.SaveChangesAsync(Ct);
            Assert.Equal(CityDeleteResult.Deleted,
                await new CityRepository(db).DeleteIfUnusedAsync(city.Id, Ct));
        }

        await using (var db = _fixture.Context(false))
        {
            AddReference(db, city.Id, reference);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            Assert.True(CityReferenceViolation.Matches(error));
        }
    }

    [Fact]
    public async Task ForeignKeyFailureRollsBackToSavepointAndLeavesTransactionUsable()
    {
        var city = new City("Save city " + Guid.NewGuid());
        await using (var db = _fixture.Context(false))
        {
            db.Cities.Add(city);
            db.Shops.Add(ShopsLiveDatabase.NewShop("Savepoint shop", city.Id));
            await db.SaveChangesAsync(Ct);
        }

        var marker = new City("Save marker " + Guid.NewGuid());
        await using (var db = _fixture.Context(false))
        await using (var tx = await db.Database.BeginTransactionAsync(Ct))
        {
            var tracked = await db.Cities.SingleAsync(c => c.Id == city.Id, Ct);
            db.Cities.Remove(tracked);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            Assert.True(CityReferenceViolation.Matches(error));
            db.Entry(tracked).State = EntityState.Unchanged;
            db.Cities.Add(marker);
            await db.SaveChangesAsync(Ct);
            await tx.CommitAsync(Ct);
        }

        await using (var db = _fixture.Context(false))
        {
            Assert.True(await db.Cities.AnyAsync(c => c.Id == city.Id, Ct));
            Assert.True(await db.Cities.AnyAsync(c => c.Id == marker.Id, Ct));
        }
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("roaster")]
    [InlineData("zone")]
    public async Task ConcurrentReferenceInsertWins_CityDeleteWaitsThenReturnsConflict(string reference)
    {
        var city = new City("Concurrent " + Guid.NewGuid());
        await using (var seed = _fixture.Context(false))
        {
            seed.Cities.Add(city);
            await seed.SaveChangesAsync(Ct);
        }

        Task<Response>? deletion = null;
        await using (var inserter = _fixture.Context(false))
        await using (var transaction = await inserter.Database.BeginTransactionAsync(Ct))
        {
            AddReference(inserter, city.Id, reference);
            await inserter.SaveChangesAsync(Ct); // FK key-share lock is held until commit.

            var committed = false;
            try
            {
                deletion = Task.Run(async () =>
                {
                    await using var db = _fixture.Context(false);
                    return await DeleteCityHandler.Handle(new DeleteCityCommand(city.Id),
                        new CityRepository(db), Mock.Of<IUnitOfWork>(), Mock.Of<ICacheService>(), Ct);
                }, Ct);

                // The precheck cannot see the uncommitted reference. Wait until DELETE reaches the
                // parent-row FK lock, then commit the insert; this exercises the actual race.
                await LivePostgresServer.WaitForLockWaitsAsync(
                    _fixture.ConnectionString, "transactionid", expected: 1, Ct);
                Assert.False(deletion.IsCompleted);
                await transaction.CommitAsync(Ct);
                committed = true;

                var response = await deletion.WaitAsync(TimeSpan.FromSeconds(15), Ct);
                Assert.False(response.IsSuccess);
                Assert.Equal(409, response.StatusCode);
            }
            finally
            {
                if (!committed)
                    await transaction.RollbackAsync(CancellationToken.None);
                if (deletion is not null)
                {
                    try { await deletion.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None); }
                    catch { /* Preserve the primary assertion or database failure. */ }
                }
            }
        }

        await using var verify = _fixture.Context(false);
        Assert.True(await verify.Cities.AnyAsync(c => c.Id == city.Id, Ct));
        var referenceExists = reference switch
        {
            "shop" => await verify.Shops.AnyAsync(s => s.Location.CityId == city.Id, Ct),
            "roaster" => await verify.Roasters.AnyAsync(r => r.Location != null && r.Location.CityId == city.Id, Ct),
            "zone" => await verify.CoffeeZones.AnyAsync(z => z.CityId == city.Id, Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(reference))
        };
        Assert.True(referenceExists, "a committed reference must not be orphaned by concurrent city deletion");
    }

    private static void AddReference(ShopsDbContext db, Guid cityId, string reference)
    {
        switch (reference)
        {
            case "shop":
                db.Shops.Add(ShopsLiveDatabase.NewShop("City reference shop", cityId));
                break;
            case "roaster":
                var roaster = new Roaster("City reference roaster");
                roaster.SetLocation(Location.CreateDraft(cityId, "Address"));
                db.Roasters.Add(roaster);
                break;
            case "zone":
                db.CoffeeZones.Add(new CoffeeZone(cityId, "City reference zone", null,
                    CoffeeZonePoint.Circle(53.9m, 27.56m, 400)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reference));
        }
    }
}
