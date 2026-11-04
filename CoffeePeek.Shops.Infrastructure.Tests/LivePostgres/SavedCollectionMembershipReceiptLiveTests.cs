using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class SavedCollectionMembershipReceiptLiveTests
{
    [Theory]
    [InlineData("coffee_shop")]
    [InlineData("roaster")]
    public async Task ReceiptUsesTheOwningTransactionAndMembershipAndHeartCommitOrRollBackTogether(string kind)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connection = await LivePostgresServer.CreateDatabaseAsync("membershipreceipt", ct);
        try
        {
            await using var writer = Context(connection);
            await writer.Database.MigrateAsync(ct);
            var (owner, target, collection) = await SeedAsync(writer, kind, ct);
            await using var reader = Context(connection);
            var store = new SavedCollectionsStore(writer, Mock.Of<IPublicAddressService>(),
                Mock.Of<ICollectionCoverStore>(), Mock.Of<IMessageBus>());
            await using (var transaction = await writer.Database.BeginTransactionAsync(ct))
            {
                var noOp = await store.SetMembershipAsync(owner, kind, "private-heart", [], ct, [], []);
                AssertReceipt(noOp, true, false, false);
                await transaction.CommitAsync(ct);
            }
            await AssertStateAsync(reader, owner, kind, target, false, true, ct);
            await using (var transaction = await writer.Database.BeginTransactionAsync(ct))
            {
                var added = await store.SetMembershipAsync(owner, kind, "private-heart", [collection], ct, [collection], []);
                AssertReceipt(added, true, true, true);
                await AssertStateAsync(reader, owner, kind, target, false, true, ct);
                await transaction.CommitAsync(ct);
            }
            await AssertStateAsync(reader, owner, kind, target, true, true, ct);
            await using (var transaction = await writer.Database.BeginTransactionAsync(ct))
            {
                var removed = await store.SetMembershipAsync(owner, kind, "private-heart", [], ct, [], [collection]);
                AssertReceipt(removed, false, true, false);
                await AssertStateAsync(reader, owner, kind, target, true, true, ct);
                await transaction.RollbackAsync(ct);
            }
            writer.ChangeTracker.Clear();
            await AssertStateAsync(reader, owner, kind, target, true, true, ct);
            await using (var transaction = await writer.Database.BeginTransactionAsync(ct))
            {
                var removed = await store.SetMembershipAsync(owner, kind, "private-heart", [], ct);
                AssertReceipt(removed, false, true, false);
                await transaction.CommitAsync(ct);
            }
            await AssertStateAsync(reader, owner, kind, target, false, false, ct);
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    private static void AssertReceipt(SavedCollectionMembershipResult result, bool favorite, bool changed, bool member)
    {
        Assert.Equal(favorite, result.IsFavorite);
        Assert.Equal(changed, result.MembershipChanged);
        Assert.Equal(member, Assert.Single(result.Collections).ContainsItem);
    }

    private static async Task AssertStateAsync(ShopsDbContext db, Guid owner, string kind, Guid target,
        bool member, bool favorite, CancellationToken ct)
    {
        Assert.Equal(member, await db.Set<SavedCollectionItem>().AsNoTracking().AnyAsync(i => i.Kind == kind && i.EntityId == target, ct));
        Assert.Equal(favorite, await db.DiscoveryFavorites.AsNoTracking()
            .AnyAsync(f => f.UserId == owner && f.Kind == kind && f.EntityId == target, ct));
    }

    private static async Task<(Guid Owner, Guid Target, Guid Collection)> SeedAsync(ShopsDbContext db, string kind, CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        Guid target;
        if (kind == "coffee_shop")
        {
            var city = new City("Receipt city");
            db.Cities.Add(city);
            var shop = new CoffeeShop(null, "Private heart", null, PriceRange.Moderate, Guid.NewGuid());
            shop.SetPublicSlug("private-heart");
            shop.SetLocation(city.Id, "Receipt street", 53.9m, 27.56m);
            db.Shops.Add(shop);
            target = shop.Id;
        }
        else
        {
            var roaster = new Roaster("Private heart");
            roaster.SetPublicSlug("private-heart");
            db.Roasters.Add(roaster);
            target = roaster.Id;
        }
        var canonical = SavedCollection.CreateList(owner, Guid.NewGuid(), "Избранное", null, "public");
        canonical.MarkDefault();
        db.SavedCollections.Add(canonical);
        db.DiscoveryFavorites.Add(new(owner, kind, target));
        await db.SaveChangesAsync(ct);
        return (owner, target, canonical.Id);
    }

    private static ShopsDbContext Context(string connection) => new(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
}
