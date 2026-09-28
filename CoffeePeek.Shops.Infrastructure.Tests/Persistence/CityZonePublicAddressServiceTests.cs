using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

// InMemory proves lifecycle/dispatch behavior only. Uniqueness, RESTRICT FKs, check constraints,
// savepoint rollback and the advisory lock are live PostgreSQL gate items.
// CoffeeZone.Polygon does not round-trip through InMemory, so zone status is always changed on the
// same tracked instance that was added, and a City is never deleted while one of its zones is tracked.
public sealed class CityZonePublicAddressServiceTests
{
    [Fact]
    public async Task City_CreateRenameRestoreDelete_KeepsStableAddress()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb(enabled: true);
        var city = new City("Минск");
        db.Cities.Add(city);
        await db.SaveChangesAsync(ct);
        var service = new PublicAddressService(db);

        var initial = await service.ResolveAsync(PublicAddressKind.City, "MINSK", ct);
        initial.Should().NotBeNull();
        initial!.Slug.Should().Be("minsk");
        initial.IsAlias.Should().BeFalse();
        initial.CanonicalPath.Should().Be("/cities/minsk");
        initial.Revision.Should().Be(1);

        city.Update("Minsk");
        await db.SaveChangesAsync(ct);
        (await service.ResolveAsync(PublicAddressKind.City, "minsk", ct))!.IsAlias.Should().BeFalse(
            "a display-name edit never renames the public address");
        (await db.CityPublicAddresses.CountAsync(ct)).Should().Be(1);

        var actor = Guid.NewGuid();
        var renamed = await service.ChangeAsync(PublicAddressKind.City, city.Id,
            new ChangePublicAddressRequest("minsk-center", 1, "Clearer"), actor, ct);
        renamed.Revision.Should().Be(2);
        var alias = await service.ResolveAsync(PublicAddressKind.City, "minsk", ct);
        alias!.IsAlias.Should().BeTrue();
        alias.Slug.Should().Be("minsk-center");

        var restored = await service.ChangeAsync(PublicAddressKind.City, city.Id,
            new ChangePublicAddressRequest("minsk", 2, "Restore"), actor, ct);
        restored.Revision.Should().Be(3);
        restored.Slug.Should().Be("minsk");

        var history = await service.HistoryAsync(PublicAddressKind.City, city.Id, 0, 10, ct);
        history.Select(x => (x.Revision, x.Action)).Should().Equal(
            (1L, "initialize"), (2L, "rename"), (3L, "rename"));
        (await db.PublicAddressAudit.AsNoTracking().Where(a => a.OwnerId == city.Id).ToArrayAsync(ct))
            .Should().NotBeEmpty().And.OnlyContain(a => a.Kind == PublicAddressKind.City);

        db.Cities.Remove(city);
        await db.SaveChangesAsync(ct);
        var rows = await db.CityPublicAddresses.AsNoTracking().ToArrayAsync(ct);
        rows.Should().NotBeEmpty().And.OnlyContain(x =>
            x.State == PublicAddressState.Tombstone && x.OwnerId == null && x.Revision == 4);
        (await service.ResolveAsync(PublicAddressKind.City, "minsk", ct)).Should().BeNull();

        var successor = new City("Минск");
        db.Cities.Add(successor);
        await db.SaveChangesAsync(ct);
        var successorAddress = (await service.GetByIdsAsync(PublicAddressKind.City, [successor.Id], ct))
            .Should().ContainSingle().Which;
        successorAddress.Slug.Should().StartWith("minsk-").And.NotBe("minsk",
            "a retired address stays permanently reserved");
    }

    [Fact]
    public async Task Zone_PublishArchiveRepublish_HidesWithoutRetiring()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb(enabled: true);
        var city = new City("Минск");
        var zone = Zone(city.Id, "Nemiga", CoffeeZoneStatus.Draft);
        db.Cities.Add(city);
        db.CoffeeZones.Add(zone);
        await db.SaveChangesAsync(ct);
        var service = new PublicAddressService(db);

        (await db.CoffeeZonePublicAddresses.CountAsync(ct)).Should().Be(0, "drafts reserve no address");
        (await service.ResolveAsync(PublicAddressKind.CoffeeZone, "nemiga", ct)).Should().BeNull();

        zone.Publish();
        await db.SaveChangesAsync(ct);
        var published = await service.ResolveAsync(PublicAddressKind.CoffeeZone, "nemiga", ct);
        published.Should().NotBeNull();
        published!.CanonicalPath.Should().Be("/coffee-zones/nemiga");
        published.Revision.Should().Be(1);
        (await service.GetByIdsAsync(PublicAddressKind.CoffeeZone, [zone.Id], ct)).Should().HaveCount(1);

        zone.Archive();
        await db.SaveChangesAsync(ct);
        (await service.ResolveAsync(PublicAddressKind.CoffeeZone, "nemiga", ct)).Should().BeNull();
        (await service.GetByIdsAsync(PublicAddressKind.CoffeeZone, [zone.Id], ct)).Should().BeEmpty();
        var hidden = (await db.CoffeeZonePublicAddresses.AsNoTracking().ToArrayAsync(ct)).Should().ContainSingle().Which;
        hidden.State.Should().Be(PublicAddressState.Canonical, "archive is reversible and must not retire the address");
        hidden.OwnerId.Should().Be(zone.Id);

        zone.MoveToDraft();
        await db.SaveChangesAsync(ct);
        (await service.ResolveAsync(PublicAddressKind.CoffeeZone, "nemiga", ct)).Should().BeNull();

        zone.Publish();
        await db.SaveChangesAsync(ct);
        var republished = await service.ResolveAsync(PublicAddressKind.CoffeeZone, "nemiga", ct);
        republished!.Slug.Should().Be("nemiga");
        republished.Revision.Should().Be(1);
        (await db.CoffeeZonePublicAddresses.CountAsync(ct)).Should().Be(1);
        (await db.PublicAddressAudit.CountAsync(a => a.OwnerId == zone.Id, ct)).Should().Be(1);
    }

    [Fact]
    public async Task DraftZone_AdminInitializeIsKeptOnPublish()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb(enabled: true);
        var city = new City("Минск");
        var zone = Zone(city.Id, "Nemiga", CoffeeZoneStatus.Draft);
        db.Cities.Add(city);
        db.CoffeeZones.Add(zone);
        await db.SaveChangesAsync(ct);
        var service = new PublicAddressService(db);

        var initialized = await service.ChangeAsync(PublicAddressKind.CoffeeZone, zone.Id,
            new ChangePublicAddressRequest("nemiga-quarter", 0, "Pre-chosen"), Guid.NewGuid(), ct);
        initialized.Slug.Should().Be("nemiga-quarter");
        initialized.Revision.Should().Be(1);
        (await service.ResolveAsync(PublicAddressKind.CoffeeZone, "nemiga-quarter", ct)).Should().BeNull(
            "a draft zone is not public even when its address is pre-initialized");
        (await service.HistoryAsync(PublicAddressKind.CoffeeZone, zone.Id, 0, 10, ct)).Should().HaveCount(1);

        zone.Publish();
        await db.SaveChangesAsync(ct);
        var published = await service.ResolveAsync(PublicAddressKind.CoffeeZone, "nemiga-quarter", ct);
        published.Should().NotBeNull();
        published!.IsAlias.Should().BeFalse();
        published.Revision.Should().Be(1);
        var rows = await db.CoffeeZonePublicAddresses.AsNoTracking().ToArrayAsync(ct);
        rows.Should().ContainSingle();
        rows.Should().NotContain(x => x.Slug == "nemiga");
    }

    [Fact]
    public async Task Namespaces_ArePerKindAndZoneNamespaceIsGlobal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb(enabled: true);
        var shop = Shop("Minsk");
        var minsk = new City("Минск");
        var grodno = new City("Гродно");
        var minskZone = Zone(minsk.Id, "Minsk", CoffeeZoneStatus.Published);
        var nemigaA = Zone(minsk.Id, "Nemiga", CoffeeZoneStatus.Published);
        var nemigaB = Zone(grodno.Id, "Nemiga", CoffeeZoneStatus.Published);
        db.Shops.Add(shop);
        db.Cities.AddRange(minsk, grodno);
        db.CoffeeZones.AddRange(minskZone, nemigaA, nemigaB);
        await db.SaveChangesAsync(ct);
        var service = new PublicAddressService(db);

        (await service.ResolveAsync(PublicAddressKind.CoffeeShop, "minsk", ct))!.CanonicalPath.Should().Be("/coffee-shops/minsk");
        (await service.ResolveAsync(PublicAddressKind.City, "minsk", ct))!.CanonicalPath.Should().Be("/cities/minsk");
        var zoneAddress = await service.ResolveAsync(PublicAddressKind.CoffeeZone, "minsk", ct);
        zoneAddress!.CanonicalPath.Should().Be("/coffee-zones/minsk");
        zoneAddress.EntityId.Should().Be(minskZone.Id);

        var nemiga = await service.GetByIdsAsync(PublicAddressKind.CoffeeZone, [nemigaA.Id, nemigaB.Id], ct);
        nemiga.Should().HaveCount(2);
        nemiga.Select(x => x.Slug).Should().OnlyHaveUniqueItems();
        nemiga.Should().ContainSingle(x => x.Slug == "nemiga");
        nemiga.Should().ContainSingle(x => x.Slug.StartsWith("nemiga-"),
            "zones share one global namespace, so a same-name zone in another city is suffixed");
    }

    [Fact]
    public async Task Backfill_CitiesAllZonesPublishedOnly_DryRunCursorReplay()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        Guid publishedZoneId;
        await using (var seed = CreateDb(enabled: false, databaseName, root))
        {
            var first = new City("Минск");
            seed.Cities.AddRange(first, new City("Минск"), new City("Брест"));
            var published = Zone(first.Id, "Nemiga", CoffeeZoneStatus.Published);
            publishedZoneId = published.Id;
            seed.CoffeeZones.AddRange(
                Zone(first.Id, "Draft Zone", CoffeeZoneStatus.Draft),
                published,
                Zone(first.Id, "Old Town", CoffeeZoneStatus.Archived));
            await seed.SaveChangesAsync(ct);
            (await seed.CityPublicAddresses.CountAsync(ct)).Should().Be(0);
            (await seed.CoffeeZonePublicAddresses.CountAsync(ct)).Should().Be(0);
        }

        await using var db = CreateDb(enabled: true, databaseName, root);
        var service = new PublicAddressService(db);
        var actor = Guid.NewGuid();

        var page = await service.BackfillAsync(PublicAddressKind.City, null, 2, dryRun: true, actor, ct);
        page.Items.Should().HaveCount(2);
        page.NextCursor.Should().NotBeNull();
        (await db.CityPublicAddresses.CountAsync(ct)).Should().Be(0);

        var dryRun = await service.BackfillAsync(PublicAddressKind.City, null, 100, dryRun: true, actor, ct);
        dryRun.Items.Should().HaveCount(3);
        dryRun.NextCursor.Should().BeNull();
        dryRun.Items.Should().ContainSingle(x => x.Slug == "minsk");
        dryRun.Items.Should().ContainSingle(x => x.Slug.StartsWith("minsk-"))
            .Which.HadCollision.Should().BeTrue("duplicate city names collide");
        dryRun.Items.Should().ContainSingle(x => x.Slug == "brest");
        (await db.CityPublicAddresses.CountAsync(ct)).Should().Be(0);

        var applied = await service.BackfillAsync(PublicAddressKind.City, null, 100, dryRun: false, actor, ct);
        applied.Items.ToDictionary(x => x.EntityId, x => x.Slug)
            .Should().BeEquivalentTo(dryRun.Items.ToDictionary(x => x.EntityId, x => x.Slug));
        (await db.CityPublicAddresses.CountAsync(ct)).Should().Be(3);
        var audit = await db.PublicAddressAudit.AsNoTracking().ToArrayAsync(ct);
        audit.Should().HaveCount(3).And.OnlyContain(a =>
            a.Kind == PublicAddressKind.City && a.Action == "initialize" && a.Reason == "Backfill");

        var replay = await service.BackfillAsync(PublicAddressKind.City, null, 100, dryRun: false, actor, ct);
        replay.Items.Should().OnlyContain(x => x.AlreadyInitialized);
        replay.Items.Select(x => x.Slug).Should().Equal(applied.Items.Select(x => x.Slug));
        (await db.CityPublicAddresses.CountAsync(ct)).Should().Be(3);

        var zones = await service.BackfillAsync(PublicAddressKind.CoffeeZone, null, 100, dryRun: false, actor, ct);
        var zoneItem = zones.Items.Should().ContainSingle("drafts and archived zones are not backfilled").Which;
        zoneItem.EntityId.Should().Be(publishedZoneId);
        zoneItem.Slug.Should().Be("nemiga");
        (await db.CoffeeZonePublicAddresses.CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task Disabled_NoAllocationAndServiceReturns503()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb(enabled: false);
        var city = new City("Минск");
        var zone = Zone(city.Id, "Nemiga", CoffeeZoneStatus.Published);
        db.Cities.Add(city);
        db.CoffeeZones.Add(zone);
        await db.SaveChangesAsync(ct);
        (await db.CityPublicAddresses.CountAsync(ct)).Should().Be(0);
        (await db.CoffeeZonePublicAddresses.CountAsync(ct)).Should().Be(0);

        var service = new PublicAddressService(db);
        var resolve = () => service.ResolveAsync(PublicAddressKind.City, "minsk", ct);
        (await resolve.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(503);
        var batch = () => service.GetByIdsAsync(PublicAddressKind.CoffeeZone, [zone.Id], ct);
        (await batch.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(503);

        var brest = new City("Брест");
        db.Cities.Add(brest);
        await db.SaveChangesAsync(ct);
        db.Cities.Remove(brest);
        await db.SaveChangesAsync(ct);
        (await db.CityPublicAddresses.CountAsync(ct)).Should().Be(0);
        (await db.CoffeeZonePublicAddresses.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task CityDelete_RetiresAddressesEvenWhenDisabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        var city = new City("Минск");
        await using (var enabled = CreateDb(enabled: true, databaseName, root))
        {
            enabled.Cities.Add(city);
            await enabled.SaveChangesAsync(ct);
            var allocated = (await enabled.CityPublicAddresses.AsNoTracking().ToArrayAsync(ct)).Should().ContainSingle().Which;
            allocated.State.Should().Be(PublicAddressState.Canonical);
            allocated.Revision.Should().Be(1);
        }

        await using var disabled = CreateDb(enabled: false, databaseName, root);
        var loaded = await disabled.Cities.SingleAsync(c => c.Id == city.Id, ct);
        disabled.Cities.Remove(loaded);
        await disabled.SaveChangesAsync(ct);

        var rows = await disabled.CityPublicAddresses.AsNoTracking().ToArrayAsync(ct);
        rows.Should().NotBeEmpty().And.OnlyContain(x =>
            x.State == PublicAddressState.Tombstone && x.OwnerId == null && x.Revision == 2);
        (await disabled.PublicAddressAudit.AsNoTracking().ToArrayAsync(ct)).Should().Contain(a =>
            a.OwnerId == city.Id && a.Kind == PublicAddressKind.City && a.Action == "retire");
    }

    [Fact]
    public async Task UnknownOwnersAndKinds_AreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb(enabled: true);
        var service = new PublicAddressService(db);

        var city = () => service.HistoryAsync(PublicAddressKind.City, Guid.NewGuid(), 0, 10, ct);
        (await city.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(404);
        var zone = () => service.HistoryAsync(PublicAddressKind.CoffeeZone, Guid.NewGuid(), 0, 10, ct);
        (await zone.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(404);
        var unknown = () => service.ResolveAsync((PublicAddressKind)99, "minsk", ct);
        (await unknown.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(400);
    }

    private static ShopsDbContext CreateDb(bool enabled, string? databaseName = null,
        InMemoryDatabaseRoot? root = null)
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString(), root ?? new InMemoryDatabaseRoot())
            .Options;
        return new ShopsDbContext(options, Options.Create(new PublicAddressOptions { Enabled = enabled }));
    }

    private static CoffeeShop Shop(string name, string? address = null)
    {
        var shop = new CoffeeShop(Guid.NewGuid(), name, null, PriceRange.Moderate, Guid.NewGuid());
        if (address is not null) shop.SetDraftLocation(Guid.NewGuid(), address);
        return shop;
    }

    private static CoffeeZone Zone(Guid cityId, string name, CoffeeZoneStatus status)
    {
        var zone = new CoffeeZone(cityId, name, null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));
        if (status == CoffeeZoneStatus.Published) zone.Publish();
        else if (status == CoffeeZoneStatus.Archived) zone.Archive();
        return zone;
    }
}
