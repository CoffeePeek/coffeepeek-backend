using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Shelf;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShelfLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private readonly ShopsLiveDatabase _fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ShelfLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        _fixture = fixture;
    }

    private static ShelfBag Bag(Guid owner, string name, string? roaster) =>
        ShelfBag.Create(Guid.NewGuid(), owner, new ShelfBagDetails(null, name, roaster, null, null, null), null, Now);

    [Fact]
    public async Task ArchiveSearchIsCaseInsensitiveOnNameOrRoasterAndTreatsWildcardsLiterally()
    {
        var owner = Guid.NewGuid();
        var lineup = new ShelfLineup(owner, []);
        var bags = new[] { Bag(owner, "Nut'n'Berry", "Sibaristica"), Bag(owner, "100% Kenya", "Tasty"), Bag(owner, "Plain", "Roaster_X") };
        foreach (var bag in bags) lineup.AddToFront(bag);
        for (var i = 0; i < bags.Length; i++) lineup.Finish(bags[i], null, null, Now.AddMinutes(i));
        await using (var db = _fixture.Context(false))
        {
            Assert.False(db.Database.HasPendingModelChanges());
            db.ShelfBags.AddRange(bags);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.Context(false))
        {
            var catalog = new Mock<IShelfCatalogLookup>();
            catalog.Setup(c => c.GetPublishedSlugsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, string>());
            var queries = new ShelfQueries(db, catalog.Object);
            Assert.Equal("Nut'n'Berry", Assert.Single((await queries.GetArchiveAsync(owner, 1, 20, "BERRY", Ct)).Items).Name);
            Assert.Equal("Nut'n'Berry", Assert.Single((await queries.GetArchiveAsync(owner, 1, 20, "sibar", Ct)).Items).Name);
            Assert.Equal("100% Kenya", Assert.Single((await queries.GetArchiveAsync(owner, 1, 20, "%", Ct)).Items).Name);
            Assert.Equal("Plain", Assert.Single((await queries.GetArchiveAsync(owner, 1, 20, "_", Ct)).Items).Name);
            var all = await queries.GetArchiveAsync(owner, 1, 20, null, Ct);
            Assert.Equal(3, all.TotalCount);
            Assert.Equal(["Plain", "100% Kenya", "Nut'n'Berry"], all.Items.Select(b => b.Name));
        }
    }

    [Fact]
    public async Task DatabaseRejectsAnOnShelfRowWithoutAPositionAndOwnerLocksAreTransactionScoped()
    {
        var owner = Guid.NewGuid();
        var bag = Bag(owner, "Constraint", null);
        new ShelfLineup(owner, []).AddToFront(bag);
        await using (var db = _fixture.Context(false))
        {
            db.ShelfBags.Add(bag);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.Context(false))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => db.ShelfBags.Where(b => b.Id == bag.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Position, (int?)null), Ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, LivePostgresServer.FindPostgres(ex)?.SqlState);
        }

        await using (var db = _fixture.Context(false))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            var repository = new ShelfBagRepository(db);
            await repository.LockOwnerAsync(owner, Ct);
            Assert.Equal([bag.Id], (await repository.GetOnShelfAsync(owner, Ct)).Select(b => b.Id));
            await transaction.CommitAsync(Ct);
        }
    }
}
