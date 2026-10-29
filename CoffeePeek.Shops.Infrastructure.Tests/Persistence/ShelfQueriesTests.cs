using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Shelf;
using Microsoft.EntityFrameworkCore;
using Moq;
using DomainStatus = CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate.ShelfBagStatus;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class ShelfQueriesTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ShelfBag Bag(Guid owner, string name, ShelfBagPhoto? photo = null) =>
        ShelfBag.Create(Guid.NewGuid(), owner, new ShelfBagDetails(null, name, null, null, null, null, null), photo, Now);

    private static IShelfCatalogLookup NoCatalog()
    {
        var catalog = new Mock<IShelfCatalogLookup>();
        catalog.Setup(c => c.GetPublishedSlugsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        return catalog.Object;
    }

    [Fact]
    public async Task ShelfArchiveAndCardReadsAreOwnerScoped()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var card = new ShelfBagPhoto(Guid.NewGuid(), "shelf-bags/card.jpg", "image/jpeg");
        var lineup = new ShelfLineup(owner, []);
        var second = Bag(owner, "second");
        var first = Bag(owner, "first", card);
        var older = Bag(owner, "finished earlier");
        var newer = Bag(owner, "finished later");
        foreach (var bag in new[] { older, newer, second, first }) lineup.AddToFront(bag);
        lineup.Finish(older, 3, false, Now.AddDays(1));
        lineup.Finish(newer, 5, true, Now.AddDays(2));
        var foreign = Bag(other, "not mine");
        new ShelfLineup(other, []).AddToFront(foreign);
        db.ShelfBags.AddRange(first, second, older, newer, foreign);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var queries = new ShelfQueries(db, NoCatalog());
        var shelf = await queries.GetShelfAsync(owner, ct);
        Assert.Equal(["first", "second"], shelf.Select(b => b.Name));
        Assert.Equal([0, 1], shelf.Select(b => b.Position!.Value));
        Assert.All(shelf, b => Assert.Equal(CoffeePeek.Contract.Enums.ShelfBagStatus.OnShelf, b.Status));

        var archive = await queries.GetArchiveAsync(owner, 1, 1, null, ct);
        Assert.Equal(2, archive.TotalCount);
        Assert.Equal("finished later", Assert.Single(archive.Items).Name);
        Assert.Null(archive.Items[0].Position);
        Assert.Equal("finished earlier", Assert.Single((await queries.GetArchiveAsync(owner, 2, 1, null, ct)).Items).Name);

        Assert.NotNull(await queries.GetPhotoAsync(owner, first.Id, card.Id, ct));
        Assert.Null(await queries.GetPhotoAsync(other, first.Id, card.Id, ct));
        Assert.Null(await queries.GetPhotoAsync(owner, first.Id, Guid.NewGuid(), ct));
        Assert.Null(await queries.GetPhotoAsync(owner, second.Id, card.Id, ct));
    }

    [Fact]
    public async Task RepositoryLoadsTheOrderedShelfAndEveryRowOfAnOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var lineup = new ShelfLineup(owner, []);
        var a = Bag(owner, "a");
        var b = Bag(owner, "b");
        lineup.AddToFront(b);
        lineup.AddToFront(a);
        lineup.Finish(b, null, null, Now);
        db.ShelfBags.AddRange(a, b);
        await db.SaveChangesAsync(ct);

        var repository = new ShelfBagRepository(db);
        Assert.Equal([a.Id], (await repository.GetOnShelfAsync(owner, ct)).Select(x => x.Id));
        Assert.Equal(2, (await repository.GetAllAsync(owner, ct)).Count);
        Assert.Equal(DomainStatus.Finished, (await repository.GetAsync(b.Id, ct))!.Status);
    }
}
