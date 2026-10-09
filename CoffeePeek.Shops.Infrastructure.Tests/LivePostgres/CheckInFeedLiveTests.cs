using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Infrastructure.Tests.Persistence;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInFeedLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    public CheckInFeedLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task PublicationPaginationAndVoteProjectionExecuteAgainstMigratedPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.Context(false);
        var city = new City("Feed city " + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("Feed " + Guid.NewGuid().ToString("N"), city.Id);
        db.Cities.Add(city); db.Shops.Add(shop); await db.SaveChangesAsync(ct);
        var time = DateTime.UtcNow.AddDays(-1); var readerId = Guid.NewGuid();
        var rows = Enumerable.Range(1, 4).Select(i => CheckInFeedReaderTests.Published(Guid.NewGuid(), shop.Id, time,
            Guid.Parse($"11111111-1111-1111-1111-{i:D12}"))).ToArray();
        db.CheckIns.AddRange(rows);
        db.CheckInHelpfulVotes.Add(new(rows[3].Id, readerId, time)); rows[3].SetHelpfulCount(1);
        await db.SaveChangesAsync(ct);
        var reader = new CheckInFeedReader(db); var scope = new CheckInFeedScope(ShopId: shop.Id);
        var first = await reader.ReadAsync(scope, 2, null, readerId, ct);
        Assert.Equal(new[] { rows[3].Id, rows[2].Id }, first.Items.Select(x => x.CheckIn.Id));
        Assert.True(first.HasMore); Assert.True(first.Items[0].CheckIn.IsHelpfulByCurrentUser);
        rows[2].Delete();
        db.CheckIns.Add(CheckInFeedReaderTests.Published(Guid.NewGuid(), shop.Id, time.AddHours(1)));
        await db.SaveChangesAsync(ct);
        var after = first.Items[^1];
        var next = await reader.ReadAsync(scope, 2, new(after.PublishedAtUtc, after.CheckIn.Id), readerId, ct);
        Assert.Equal(new[] { rows[1].Id, rows[0].Id }, next.Items.Select(x => x.CheckIn.Id)); Assert.False(next.HasMore);
        shop.SetHidden(true); await db.SaveChangesAsync(ct);
        Assert.Empty((await reader.ReadAsync(scope, 20, null, readerId, ct)).Items);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
