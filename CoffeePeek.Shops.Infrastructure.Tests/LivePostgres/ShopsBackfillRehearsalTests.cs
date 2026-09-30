using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopsBackfillRehearsalTests : IClassFixture<ShopsBackfillRehearsalDatabase>
{
    private readonly ShopsBackfillRehearsalDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public ShopsBackfillRehearsalTests(ShopsBackfillRehearsalDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task LegacyDatabase_MigratesIncrementallyWithoutLosingOwners()
    {
        fixture.BeforeMigrations.Last().Should().Be("20260923120247_AddCoffeeZonePolygon");
        fixture.BeforeMigrations.Should().NotContain("20260927112559_AddPublicAddressRegistries");
        fixture.RegistryExistedBefore.Should().BeFalse();
        await using var db = fixture.Context(false);
        (await db.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(Ct)).Should().Equal(db.Database.GetMigrations());
        db.Database.HasPendingModelChanges().Should().BeFalse();
        (await db.Shops.CountAsync(x => fixture.ShopIds.Contains(x.Id), Ct)).Should().Be(3);
        (await db.Roasters.CountAsync(x => fixture.RoasterIds.Contains(x.Id), Ct)).Should().Be(3);
        (await db.CoffeeZones.CountAsync(x => fixture.ZoneIds.Contains(x.Id), Ct)).Should().Be(2);
    }

    [Fact]
    public async Task FourKindBackfill_ReviewsEachPageReplaysHandlesConflictAndRehearsesRollback()
    {
        var actor = Guid.NewGuid();
        await using var db = fixture.Context();
        var service = new PublicAddressService(db);
        (await db.PublicAddressAudit.CountAsync(Ct)).Should().Be(0);
        var expected = new Dictionary<PublicAddressKind, Guid[]>
        {
            [PublicAddressKind.CoffeeShop] = fixture.ShopIds,
            [PublicAddressKind.Roaster] = fixture.RoasterIds,
            [PublicAddressKind.City] = fixture.CityIds,
            [PublicAddressKind.CoffeeZone] = fixture.ZoneIds
        };
        foreach (var (kind, ids) in expected)
        {
            var previewOnly = new List<PublicAddressBackfillItem>();
            Guid? cursor = null;
            do
            {
                var page = await service.BackfillAsync(kind, cursor, 1, true, actor, Ct);
                page.Items.Should().ContainSingle();
                previewOnly.AddRange(page.Items);
                previewOnly.Count.Should().BeLessThanOrEqualTo(ids.Length);
                cursor = page.NextCursor;
            } while (cursor is not null);
            previewOnly.Select(x => x.EntityId).Should().BeEquivalentTo(ids);
            if (kind == PublicAddressKind.City)
                previewOnly.Count(x => x.Slug == "minsk" && !x.HadCollision).Should().Be(2,
                    "dry-run pages do not reserve earlier proposals; write each reviewed page before previewing the next");
            var written = new List<PublicAddressBackfillItem>();
            cursor = null;
            do
            {
                var count = await db.PublicAddressAudit.CountAsync(Ct);
                var preview = await service.BackfillAsync(kind, cursor, 2, true, actor, Ct);
                (await db.PublicAddressAudit.CountAsync(Ct)).Should().Be(count);
                var applied = await service.BackfillAsync(kind, cursor, 2, false, actor, Ct);
                applied.Items.Length.Should().BeInRange(1, 2);
                applied.Items.Should().BeEquivalentTo(preview.Items, options => options.WithStrictOrdering());
                applied.Items.Should().OnlyContain(x => !x.AlreadyInitialized);
                applied.NextCursor.Should().Be(preview.NextCursor);
                written.AddRange(applied.Items);
                written.Count.Should().BeLessThanOrEqualTo(ids.Length);
                cursor = applied.NextCursor;
            } while (cursor is not null);
            written.Select(x => x.EntityId).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(ids);
            written.Count(x => x.HadCollision).Should().Be(1);
            var replay = await service.BackfillAsync(kind, null, 100, false, actor, Ct);
            replay.Items.Should().OnlyContain(x => x.AlreadyInitialized);
            replay.Items.Select(x => (x.EntityId, x.Slug, x.HadCollision)).Should()
                .Equal(written.Select(x => (x.EntityId, x.Slug, x.HadCollision)));
            (await db.PublicAddressAudit.CountAsync(x => x.Kind == kind, Ct)).Should().Be(ids.Length);
        }
        (await service.GetByIdsAsync(PublicAddressKind.CoffeeShop, [fixture.HiddenShopId], Ct)).Should().BeEmpty();
        (await db.CoffeeZonePublicAddresses.AnyAsync(x => x.OwnerId == fixture.DraftZoneId, Ct)).Should().BeFalse();
        var conflictA = await fixture.AddOwnerAsync(PublicAddressKind.City, "Backfill conflict", false, Ct);
        var conflictB = await fixture.AddOwnerAsync(PublicAddressKind.City, "Backfill safe", false, Ct);
        var before = await service.BackfillAsync(PublicAddressKind.City, null, 100, true, actor, Ct);
        before.Items.Count(x => !x.AlreadyInitialized).Should().Be(2);
        var proposal = before.Items.Single(x => x.EntityId == conflictA).Slug;
        (await db.CityPublicAddresses.CountAsync(x => x.OwnerId == conflictA || x.OwnerId == conflictB, Ct)).Should().Be(0);
        await using var blocker = await LivePostgresServer.BeginUncommittedTombstoneAsync(fixture.ConnectionString, "CityPublicAddresses", proposal, Ct);
        var waiting = service.BackfillAsync(PublicAddressKind.City, null, 100, false, actor, Ct);
        try
        {
            await LivePostgresServer.WaitForLockWaitsAsync(fixture.ConnectionString, "transactionid", 1, Ct);
            waiting.IsCompleted.Should().BeFalse();
            await blocker.CommitAsync(Ct);
            var error = await Assert.ThrowsAsync<PublicAddressException>(() => waiting);
            error.StatusCode.Should().Be(409);
            error.Message.Should().Be("Address is reserved or changed concurrently.");
        }
        finally
        {
            await blocker.DisposeAsync();
            try { await waiting; } catch { }
        }
        (await db.CityPublicAddresses.CountAsync(x => x.OwnerId == conflictA || x.OwnerId == conflictB, Ct)).Should().Be(0);
        (await db.PublicAddressAudit.CountAsync(x => x.OwnerId == conflictA || x.OwnerId == conflictB, Ct)).Should().Be(0);
        var retryPreview = await service.BackfillAsync(PublicAddressKind.City, null, 100, true, actor, Ct);
        retryPreview.Items.Single(x => x.EntityId == conflictA).Slug.Should().Be(PublicSlug.Candidate("Backfill conflict", conflictA, 1));
        var retry = await service.BackfillAsync(PublicAddressKind.City, null, 100, false, actor, Ct);
        retry.Items.Should().BeEquivalentTo(retryPreview.Items, options => options.WithStrictOrdering());
        await using var disabled = fixture.Context(false);
        var off = new PublicAddressService(disabled);
        await Disabled(() => off.ResolveAsync(PublicAddressKind.City, "minsk", Ct));
        await Disabled(() => off.GetByIdsAsync(PublicAddressKind.City, [conflictA], Ct));
        await Disabled(() => off.BackfillAsync(PublicAddressKind.City, null, 100, true, actor, Ct));
        await Disabled(() => off.ChangeAsync(PublicAddressKind.City, conflictA, new("unused", 1, "live"), actor, Ct));
        await Disabled(() => off.HistoryAsync(PublicAddressKind.City, conflictA, 0, 100, Ct));
        var retiredSlug = retry.Items.Single(x => x.EntityId == conflictB).Slug;
        disabled.Cities.Remove(await disabled.Cities.SingleAsync(x => x.Id == conflictB, Ct));
        await disabled.SaveChangesAsync(Ct);
        var added = new City("Rollback newcomer");
        disabled.Cities.Add(added);
        await disabled.SaveChangesAsync(Ct);
        (await disabled.CityPublicAddresses.AnyAsync(x => x.OwnerId == added.Id, Ct)).Should().BeFalse();
        (await service.ResolveAsync(PublicAddressKind.City, retiredSlug, Ct)).Should().BeNull();
        (await service.ResolveAsync(PublicAddressKind.City, "minsk", Ct)).Should().NotBeNull();
        var row = await db.CityPublicAddresses.AsNoTracking().SingleAsync(x => x.Slug == retiredSlug, Ct);
        row.State.Should().Be(PublicAddressState.Tombstone);
        row.OwnerId.Should().BeNull();
        (await service.BackfillAsync(PublicAddressKind.City, null, 100, false, actor, Ct)).Items
            .Should().ContainSingle(x => x.EntityId == added.Id && !x.AlreadyInitialized);
        // Rollback toggles the flag; migration Down would erase permanent reservations.
    }

    private static async Task Disabled(Func<Task> action) =>
        (await Assert.ThrowsAsync<PublicAddressException>(action)).StatusCode.Should().Be(503);
}
