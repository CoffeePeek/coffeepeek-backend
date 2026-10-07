using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class CheckInFeedReaderTests
{
    public static CheckIn Published(Guid userId, Guid shopId, DateTime publishedAt, Guid? id = null)
    {
        var row = CheckIn.CreateUnified(userId, shopId, "author", "Good coffee", Rating.Create(4, 5, 3),
            DateTime.UtcNow.AddDays(-3), CheckInVisibility.Public, id: id);
        row.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, publishedAt);
        return row;
    }

    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static CoffeeShop Shop(Guid? cityId = null)
    {
        var shop = new CoffeeShop(null, "Feed shop", null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetLocation(cityId ?? Guid.NewGuid(), "Address", 53m, 27m);
        return shop;
    }

    [Fact]
    public async Task FeedContainsOnlyCurrentApprovedPublicContentEvenForItsAuthor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var shop = Shop(); var hiddenShop = Shop(); hiddenShop.SetHidden(true);
        db.Shops.AddRange(shop, hiddenShop);
        var owner = Guid.NewGuid(); var time = DateTime.UtcNow.AddDays(-1);
        var visible = Published(owner, shop.Id, time);
        var privateRow = Published(owner, shop.Id, time); privateRow.ChangeVisibility(CheckInVisibility.Private);
        var deleted = Published(owner, shop.Id, time); deleted.Delete();
        var pending = Published(owner, shop.Id, time); pending.UpdateContent("Updated text", Rating.Create(5, 5, 5));
        var rejected = Published(owner, shop.Id, time);
        rejected.ApplyModerationDecision(1, rejected.ModerationSubmissionId!.Value, 2, CheckInModerationState.Rejected, "Rejected", time);
        var neverPublished = CheckIn.CreateUnified(owner, shop.Id, "author", "Pending", Rating.Create(3, 3, 3), time, CheckInVisibility.Public);
        db.CheckIns.AddRange(visible, privateRow, deleted, pending, rejected, neverPublished,
            Published(owner, hiddenShop.Id, time), Published(owner, Guid.NewGuid(), time));
        await db.SaveChangesAsync(ct);
        var page = await new CheckInFeedReader(db).ReadAsync(new(), 20, null, owner, ct);
        Assert.Equal(visible.Id, Assert.Single(page.Items).CheckIn.Id);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task FirstPublicationOrdersDelayedApprovalsAndDoesNotBumpEdits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database(); var shop = Shop(); db.Shops.Add(shop);
        var time = DateTime.UtcNow.AddDays(-1);
        var delayed = Published(Guid.NewGuid(), shop.Id, time.AddHours(2)); delayed.CreatedAtUtc = time.AddDays(-2);
        var recentCreation = Published(Guid.NewGuid(), shop.Id, time); recentCreation.CreatedAtUtc = time.AddHours(1);
        db.CheckIns.AddRange(delayed, recentCreation); await db.SaveChangesAsync(ct);
        var reader = new CheckInFeedReader(db);
        Assert.Equal(new[] { delayed.Id, recentCreation.Id }, (await reader.ReadAsync(new(), 20, null, null, ct)).Items.Select(x => x.CheckIn.Id));
        recentCreation.UpdateContent("Edited", Rating.Create(5, 5, 5));
        recentCreation.ApplyModerationDecision(2, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, time.AddHours(3));
        await db.SaveChangesAsync(ct);
        var page = await reader.ReadAsync(new(), 20, null, null, ct);
        Assert.Equal(new[] { delayed.Id, recentCreation.Id }, page.Items.Select(x => x.CheckIn.Id));
        Assert.Equal(time, page.Items[1].PublishedAtUtc);
    }

    [Fact]
    public async Task KeysetPaginationSurvivesTiedTimesNewPublicationsAndDeletedAnchor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database(); var shop = Shop(); db.Shops.Add(shop);
        var time = DateTime.UtcNow.AddDays(-1); var author = Guid.NewGuid();
        var rows = Enumerable.Range(1, 4).Select(i => Published(author, shop.Id, time, Guid.Parse($"00000000-0000-0000-0000-{i:D12}"))).ToArray();
        db.CheckIns.AddRange(rows); await db.SaveChangesAsync(ct);
        var reader = new CheckInFeedReader(db);
        var first = await reader.ReadAsync(new(), 2, null, null, ct);
        Assert.True(first.HasMore);
        Assert.Equal(new[] { rows[3].Id, rows[2].Id }, first.Items.Select(x => x.CheckIn.Id));
        rows[2].Delete();
        var newest = Published(Guid.NewGuid(), shop.Id, time.AddHours(1)); db.CheckIns.Add(newest);
        await db.SaveChangesAsync(ct);
        var last = first.Items[^1];
        var next = await reader.ReadAsync(new(), 2, new(last.PublishedAtUtc, last.CheckIn.Id), null, ct);
        Assert.Equal(new[] { rows[1].Id, rows[0].Id }, next.Items.Select(x => x.CheckIn.Id));
        Assert.False(next.HasMore);
        Assert.DoesNotContain(newest.Id, next.Items.Select(x => x.CheckIn.Id));
        Assert.Equal(newest.Id, (await reader.ReadAsync(new(), 2, null, null, ct)).Items[0].CheckIn.Id);
    }

    [Fact]
    public async Task FiltersComposeAndVotesArePersonalizedWithoutChangingFeedMembership()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database(); var city = Guid.NewGuid(); var shop = Shop(city); var otherShop = Shop();
        db.Shops.AddRange(shop, otherShop); var author = Guid.NewGuid(); var readerId = Guid.NewGuid();
        var time = DateTime.UtcNow.AddDays(-1);
        var visible = Published(author, shop.Id, time); visible.SetHelpfulCount(1);
        db.CheckIns.AddRange(visible, Published(Guid.NewGuid(), shop.Id, time), Published(author, otherShop.Id, time));
        db.CheckInHelpfulVotes.Add(new(visible.Id, readerId, time)); await db.SaveChangesAsync(ct);
        var reader = new CheckInFeedReader(db); var scope = new CheckInFeedScope(city, shop.Id, author);
        var loggedIn = Assert.Single((await reader.ReadAsync(scope, 20, null, readerId, ct)).Items).CheckIn;
        Assert.Equal(visible.Id, loggedIn.Id); Assert.True(loggedIn.IsHelpfulByCurrentUser); Assert.Equal(1, loggedIn.HelpfulCount);
        var anonymous = Assert.Single((await reader.ReadAsync(scope, 20, null, null, ct)).Items).CheckIn;
        Assert.Equal(visible.Id, anonymous.Id); Assert.False(anonymous.IsHelpfulByCurrentUser);
        Assert.Empty((await reader.ReadAsync(scope with { CityId = Guid.NewGuid() }, 20, null, null, ct)).Items);
    }

    [Fact]
    public void FeedQueryTranslatesToPostgresAndHasMatchingPartialDescendingIndex()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=test;Password=test").Options);
        var scope = new CheckInFeedScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var sql = CheckInFeedReader.BuildQuery(db, scope, new(DateTime.UtcNow, Guid.NewGuid())).Take(21).ToQueryString();
        Assert.Contains("FirstPublishedAtUtc", sql); Assert.Contains("ApprovedRevision", sql);
        Assert.Contains("ORDER BY", sql); Assert.Contains("DESC", sql); Assert.Contains("LIMIT", sql); Assert.DoesNotContain("OFFSET", sql);
        var index = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CheckIn))!.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(CheckIn.FirstPublishedAtUtc), nameof(CheckIn.Id) }));
        Assert.True(index.IsDescending is { Count: 0 } || index.IsDescending!.All(x => x));
        Assert.Contains("ApprovedRevision", index.GetFilter());
    }
}
