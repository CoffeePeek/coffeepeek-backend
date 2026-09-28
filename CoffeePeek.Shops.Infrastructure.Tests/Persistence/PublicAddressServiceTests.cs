using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class PublicAddressServiceTests
{
    [Fact]
    public async Task SaveRenameRestoreDelete_MaintainsOwnerWideLifecycle()
    {
        await using var db = CreateDb(enabled: true);
        var shop = Shop("Zёrna", "Октябрьская 16");
        db.Shops.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new PublicAddressService(db);
        var initial = await service.ResolveAsync(PublicAddressKind.CoffeeShop,
            "ZYORNA-OKTYABRSKAYA-16", TestContext.Current.CancellationToken);
        initial.Should().NotBeNull();
        initial!.Slug.Should().Be("zyorna-oktyabrskaya-16");
        initial.IsAlias.Should().BeFalse("case normalization does not make the canonical address an alias");

        var actor = Guid.NewGuid();
        var renamed = await service.ChangeAsync(PublicAddressKind.CoffeeShop, shop.Id,
            new ChangePublicAddressRequest("zyorna-center", 1, "Clearer public address"), actor,
            TestContext.Current.CancellationToken);
        renamed.Revision.Should().Be(2);
        (await service.ResolveAsync(PublicAddressKind.CoffeeShop, initial.Slug,
            TestContext.Current.CancellationToken))!.IsAlias.Should().BeTrue();

        var restored = await service.ChangeAsync(PublicAddressKind.CoffeeShop, shop.Id,
            new ChangePublicAddressRequest(initial.Slug, 2, "Restore original address"), actor,
            TestContext.Current.CancellationToken);
        restored.Revision.Should().Be(3);
        restored.Slug.Should().Be(initial.Slug);

        var history = await service.HistoryAsync(PublicAddressKind.CoffeeShop, shop.Id, 0, 10,
            TestContext.Current.CancellationToken);
        history.Select(x => (x.Revision, x.Action)).Should().Equal(
            (1L, "initialize"), (2L, "rename"), (3L, "rename"));

        db.Shops.Remove(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var rows = await db.CoffeeShopPublicAddresses.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken);
        rows.Should().HaveCount(2).And.OnlyContain(x =>
            x.State == PublicAddressState.Tombstone && x.OwnerId == null && x.Revision == 4);
        (await service.ResolveAsync(PublicAddressKind.CoffeeShop, initial.Slug,
            TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAndBatch_DoNotExposeHiddenShop()
    {
        await using var db = CreateDb(enabled: true);
        var shop = Shop("Hidden Coffee");
        shop.SetHidden(true);
        db.Shops.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new PublicAddressService(db);

        (await service.ResolveAsync(PublicAddressKind.CoffeeShop, "hidden-coffee",
            TestContext.Current.CancellationToken)).Should().BeNull();
        (await service.GetByIdsAsync(PublicAddressKind.CoffeeShop, [shop.Id],
            TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Backfill_DryRunIsBoundedAndReplayDoesNotRename()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        await using (var seed = CreateDb(enabled: false, databaseName, root))
        {
            seed.Roasters.AddRange(new Roaster("Same Name"), new Roaster("Same Name"), new Roaster("Same Name"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var db = CreateDb(enabled: true, databaseName, root);
        var service = new PublicAddressService(db);
        var actor = Guid.NewGuid();
        var dryRun = await service.BackfillAsync(PublicAddressKind.Roaster, null, 2, true, actor,
            TestContext.Current.CancellationToken);
        dryRun.Items.Should().HaveCount(2);
        dryRun.NextCursor.Should().NotBeNull();
        dryRun.Items.Should().ContainSingle(x => x.HadCollision);
        (await db.RoasterPublicAddresses.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        var applied = await service.BackfillAsync(PublicAddressKind.Roaster, null, 2, false, actor,
            TestContext.Current.CancellationToken);
        var replay = await service.BackfillAsync(PublicAddressKind.Roaster, null, 2, false, actor,
            TestContext.Current.CancellationToken);
        replay.Items.Should().OnlyContain(x => x.AlreadyInitialized);
        replay.Items.Select(x => x.Slug).Should().Equal(applied.Items.Select(x => x.Slug));
        (await db.RoasterPublicAddresses.CountAsync(TestContext.Current.CancellationToken)).Should().Be(2);
    }

    [Fact]
    public async Task History_UnknownOwnerReturnsNotFound()
    {
        await using var db = CreateDb(enabled: true);
        var action = () => new PublicAddressService(db).HistoryAsync(PublicAddressKind.CoffeeShop,
            Guid.NewGuid(), 0, 10, TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(404);
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
}
