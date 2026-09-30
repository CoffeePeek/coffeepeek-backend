using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopsLiveTransactionTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public ShopsLiveTransactionTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task CatalogBackfillAndBatchFilters_UsePersistedCanonicalAndAliasAddresses()
    {
        foreach (var kind in new[] { PublicAddressKind.CoffeeBean, PublicAddressKind.Equipment, PublicAddressKind.BrewMethod, PublicAddressKind.ShopTag })
        {
            var id = await fixture.AddOwnerAsync(kind, "legacy catalog " + kind, false, Ct);
            await using var db = fixture.Context();
            var service = new PublicAddressService(db);
            var preview = await service.BackfillAsync(kind, null, 100, true, Guid.NewGuid(), Ct);
            var suggested = preview.Items.Single(x => x.EntityId == id).Slug;
            (await service.GetByIdsAsync(kind, [id], Ct)).Should().BeEmpty();
            await service.BackfillAsync(kind, null, 100, false, Guid.NewGuid(), Ct);
            var current = (await service.GetForReadAsync(kind, [id], Ct)).Single();
            current.Slug.Should().Be(suggested);
            await service.ChangeAsync(kind, id, new(suggested + "-new", 1, "Live catalog alias"), Guid.NewGuid(), Ct);
            (await service.ResolveIdsAsync(kind, [suggested.ToUpperInvariant(), suggested + "-new"], Ct)).Should().Equal(id);
            (await service.ResolveAsync(kind, suggested, Ct))!.IsAlias.Should().BeTrue();
            var unknown = () => service.ResolveIdsAsync(kind, ["missing-public-catalog-value"], Ct);
            (await unknown.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(404);
        }
    }

    [Fact]
    public async Task FailedOwnerSave_RollsBackAddressesAndAuditInsideAmbientTransaction()
    {
        var original = ShopsLiveDatabase.NewShop("duplicate moderation original");
        await using (var seed = fixture.Context(false))
        {
            seed.Shops.Add(original);
            await seed.SaveChangesAsync(Ct);
        }
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var failing = new CoffeeShop(Guid.NewGuid(), "duplicate moderation failing", null, PriceRange.Moderate, original.ModerationId!.Value);
        failing.SetDraftLocation(Guid.NewGuid(), "live address");
        db.Shops.Add(failing);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        LivePostgresServer.FindPostgres(error)!.ConstraintName.Should().Be("IX_Shops_ModerationId");
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.CoffeeShopPublicAddresses.AnyAsync(x => x.OwnerId == failing.Id, Ct)).Should().BeFalse();
        (await db.PublicAddressAudit.AnyAsync(x => x.OwnerId == failing.Id, Ct)).Should().BeFalse();
        var good = new City("savepoint usable");
        db.Cities.Add(good);
        await db.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
        await using var check = fixture.Context();
        (await check.Cities.AnyAsync(x => x.Id == good.Id, Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCityWithZones_Returns409AndRollsBackRetirement()
    {
        var id = await fixture.AddOwnerAsync(PublicAddressKind.CoffeeZone, "restrict zone", true, Ct);
        await using var db = fixture.Context();
        var zone = await db.CoffeeZones.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var error = await Assert.ThrowsAsync<ConflictException>(() => DeleteCityHandler.Handle(new(zone.CityId),
            new CityRepository(db), new UnitOfWork<CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext>(db), Mock.Of<ICacheService>(), Ct));
        LivePostgresServer.FindPostgres(error)!.SqlState.Should().Be("23503");
        LivePostgresServer.FindPostgres(error)!.ConstraintName.Should().Be("FK_CoffeeZones_Cities_CityId");
        (await db.CityPublicAddresses.SingleAsync(x => x.OwnerId == zone.CityId, Ct)).State.Should().Be(PublicAddressState.Canonical);
        (await db.PublicAddressAudit.CountAsync(x => x.OwnerId == zone.CityId && x.Action == "retire", Ct)).Should().Be(0);
        await transaction.CommitAsync(Ct);
    }

    [Fact]
    public async Task ZonePolygon_RoundTripsAndArchiveRepublishPreservesAddress()
    {
        var cityId = await fixture.AddOwnerAsync(PublicAddressKind.City, "polygon city", true, Ct);
        var zone = ShopsLiveDatabase.NewZone(cityId, "polygon zone");
        await using (var seed = fixture.Context())
        {
            seed.CoffeeZones.Add(zone);
            await seed.SaveChangesAsync(Ct);
            (await seed.CoffeeZonePublicAddresses.AnyAsync(x => x.OwnerId == zone.Id, Ct)).Should().BeFalse();
        }
        await using var db = fixture.Context();
        var loaded = await db.CoffeeZones.SingleAsync(x => x.Id == zone.Id, Ct);
        loaded.Polygon.Should().BeEquivalentTo(zone.Polygon);
        loaded.Publish();
        await db.SaveChangesAsync(Ct);
        var original = await db.CoffeeZonePublicAddresses.AsNoTracking().SingleAsync(x => x.OwnerId == zone.Id, Ct);
        var service = new PublicAddressService(db);
        (await service.ResolveAsync(PublicAddressKind.CoffeeZone, original.Slug, Ct)).Should().NotBeNull();
        loaded.Archive();
        await db.SaveChangesAsync(Ct);
        (await service.ResolveAsync(PublicAddressKind.CoffeeZone, original.Slug, Ct)).Should().BeNull();
        loaded.Publish();
        await db.SaveChangesAsync(Ct);
        var current = await db.CoffeeZonePublicAddresses.AsNoTracking().SingleAsync(x => x.OwnerId == zone.Id, Ct);
        current.Id.Should().Be(original.Id);
        current.Revision.Should().Be(1);
        (await service.ResolveAsync(PublicAddressKind.CoffeeZone, current.Slug, Ct)).Should().NotBeNull();
    }
}
