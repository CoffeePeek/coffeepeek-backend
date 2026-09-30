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
public sealed class ShopsLiveConcurrencyTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public ShopsLiveConcurrencyTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentCreation_WaitsOnAdvisoryLockAndAllocatesDistinctSlugs()
    {
        await using var first = fixture.Context();
        await using var second = fixture.Context();
        await using var transaction = await first.Database.BeginTransactionAsync(Ct);
        var a = new City("Минск");
        var b = new City("Минск");
        first.Cities.Add(a);
        await first.SaveChangesAsync(Ct);
        second.Cities.Add(b);
        var waiting = second.SaveChangesAsync(Ct);
        try
        {
            await LivePostgresServer.WaitForAdvisoryWaitersAsync(fixture.ConnectionString, LivePostgresServer.ShopsLockKey, 1, Ct);
            waiting.IsCompleted.Should().BeFalse();
            await transaction.CommitAsync(Ct);
            await waiting;
        }
        finally
        {
            await transaction.DisposeAsync();
            try { await waiting; } catch { /* The original failure is retained. */ }
        }
        await using var check = fixture.Context();
        (await check.CityPublicAddresses.SingleAsync(x => x.OwnerId == a.Id, Ct)).Slug.Should().Be("minsk");
        (await check.CityPublicAddresses.SingleAsync(x => x.OwnerId == b.Id, Ct)).Slug.Should().Be(PublicSlug.Candidate("Минск", b.Id, 1));
    }

    [Fact]
    public async Task ConcurrentRename_OneSucceedsAndStaleRevisionReturns409()
    {
        var id = await fixture.AddOwnerAsync(PublicAddressKind.CoffeeShop, "concurrent rename", false, Ct);
        await using var first = fixture.Context();
        await using var second = fixture.Context();
        var actor = Guid.NewGuid();
        await new PublicAddressService(first).ChangeAsync(PublicAddressKind.CoffeeShop, id,
            new("concurrent-start", 0, "live gate"), actor, Ct);
        await using var transaction = await first.Database.BeginTransactionAsync(Ct);
        var result = await new PublicAddressService(first).ChangeAsync(PublicAddressKind.CoffeeShop, id,
            new("concurrent-winner", 1, "live gate"), actor, Ct);
        result.Revision.Should().Be(2);
        var waiting = new PublicAddressService(second).ChangeAsync(PublicAddressKind.CoffeeShop, id,
            new("concurrent-loser", 1, "live gate"), actor, Ct);
        try
        {
            await LivePostgresServer.WaitForAdvisoryWaitersAsync(fixture.ConnectionString, LivePostgresServer.ShopsLockKey, 1, Ct);
            waiting.IsCompleted.Should().BeFalse();
            await transaction.CommitAsync(Ct);
            (await Assert.ThrowsAsync<PublicAddressException>(() => waiting)).StatusCode.Should().Be(409);
        }
        finally
        {
            await transaction.DisposeAsync();
            try { await waiting; } catch { }
        }
        await using var check = fixture.Context();
        (await check.CoffeeShopPublicAddresses.SingleAsync(x => x.OwnerId == id && x.State == PublicAddressState.Canonical, Ct)).Slug.Should().Be("concurrent-winner");
        (await check.PublicAddressAudit.CountAsync(x => x.OwnerId == id, Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Rename_AtoBtoA_RestoresOriginalRowWithoutUniqueOwnerViolation()
    {
        var id = await fixture.AddOwnerAsync(PublicAddressKind.CoffeeShop, "restore rename", false, Ct);
        await using var db = fixture.Context();
        var service = new PublicAddressService(db);
        var actor = Guid.NewGuid();
        await service.ChangeAsync(PublicAddressKind.CoffeeShop, id, new("restore-alpha", 0, "live gate"), actor, Ct);
        var original = await db.CoffeeShopPublicAddresses.AsNoTracking().SingleAsync(x => x.OwnerId == id, Ct);
        await service.ChangeAsync(PublicAddressKind.CoffeeShop, id, new("restore-beta", 1, "live gate"), actor, Ct);
        await service.ChangeAsync(PublicAddressKind.CoffeeShop, id, new("restore-alpha", 2, "live gate"), actor, Ct);
        var rows = await db.CoffeeShopPublicAddresses.AsNoTracking().Where(x => x.OwnerId == id).ToArrayAsync(Ct);
        rows.Should().HaveCount(2);
        rows.Single(x => x.State == PublicAddressState.Canonical).Id.Should().Be(original.Id);
        rows.Single(x => x.State == PublicAddressState.Canonical).Revision.Should().Be(3);
        (await db.PublicAddressAudit.Where(x => x.OwnerId == id).OrderBy(x => x.Revision).Select(x => x.Action).ToArrayAsync(Ct))
            .Should().Equal("initialize", "rename", "rename");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightRenameConflict_RollsBackDemotionAndKeepsAmbientTransactionUsable(bool ambient)
    {
        var stem = ambient ? "ambient" : "owned";
        var id = await fixture.AddOwnerAsync(PublicAddressKind.CoffeeShop, stem + " conflict", false, Ct);
        await using var db = fixture.Context();
        var service = new PublicAddressService(db);
        await service.ChangeAsync(PublicAddressKind.CoffeeShop, id, new(stem + "-original", 0, "live gate"), Guid.NewGuid(), Ct);
        await using var transaction = ambient ? await db.Database.BeginTransactionAsync(Ct) : null;
        await using var blocker = await LivePostgresServer.BeginUncommittedTombstoneAsync(fixture.ConnectionString,
            "CoffeeShopPublicAddresses", stem + "-conflict", Ct);
        var waiting = service.ChangeAsync(PublicAddressKind.CoffeeShop, id,
            new(stem + "-conflict", 1, "live gate"), Guid.NewGuid(), Ct);
        try
        {
            await LivePostgresServer.WaitForLockWaitsAsync(fixture.ConnectionString, "transactionid", 1, Ct);
            waiting.IsCompleted.Should().BeFalse();
            await blocker.CommitAsync(Ct);
            (await Assert.ThrowsAsync<PublicAddressException>(() => waiting)).StatusCode.Should().Be(409);
        }
        finally
        {
            await blocker.DisposeAsync();
            try { await waiting; } catch { }
        }
        db.ChangeTracker.Entries().Should().BeEmpty();
        var row = await db.CoffeeShopPublicAddresses.AsNoTracking().SingleAsync(x => x.OwnerId == id, Ct);
        row.Slug.Should().Be(stem + "-original");
        row.State.Should().Be(PublicAddressState.Canonical);
        row.Revision.Should().Be(1);
        (await db.PublicAddressAudit.CountAsync(x => x.OwnerId == id, Ct)).Should().Be(1);
        if (transaction is not null)
        {
            db.Cities.Add(new City(stem + " usable"));
            await db.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }
    }
}
