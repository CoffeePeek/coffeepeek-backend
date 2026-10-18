using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Application.Features.Feed;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class PublicHomeCheckInTests
{
    [Fact]
    public async Task PublicationRechecksVisibilityRevisionAndPhotosWithoutCreatingAShopOrRating()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = Guid.NewGuid();
        var row = CheckIn.CreateHome(owner, "author", "Home brew", DateTime.UtcNow.AddDays(-1),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), "aeropress", null, "Custom beans", 18m, 120, 5,
            visibility: CheckInVisibility.Public);
        row.AddPhotos([new CheckInPhoto("cup.jpg", "image/jpeg", "private/home", 10, owner)]);
        db.CheckIns.Add(row);
        await db.SaveChangesAsync(ct);
        var queries = new CheckInQueries(db, Moq.Mock.Of<CoffeePeek.Shops.Application.Abstractions.IAuthorVisibilityLookup>());
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, null, ct));
        Assert.Null(await queries.GetHomeByIdAsync(row.Id, Guid.NewGuid(), ct));
        Assert.NotNull(await queries.GetModerationSnapshotAsync(row.Id, row.ContentRevision, ct));
        Assert.NotNull(await queries.GetHomeByIdAsync(row.Id, owner, ct));

        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        var item = Assert.Single((await queries.GetPublicAsync(null, null, null, null, 10, null, ct)).Items);
        Assert.Equal(CoffeePeek.Contract.Enums.CheckInKind.Home, item.Kind);
        Assert.Null(item.ShopId);
        Assert.Null(item.Rating);
        Assert.Equal(5, item.Home!.TasteScore);
        Assert.Equal("Custom beans", item.Home.BeanName);
        Assert.Single(item.Photos);
        Assert.NotNull(await queries.GetUnifiedByIdAsync(row.Id, null, ct));
        Assert.NotNull(await queries.GetHomeByIdAsync(row.Id, null, ct));
        Assert.Empty(await CheckInEligibility.LatestRatings(db).ToArrayAsync(ct));
        Assert.Empty((await queries.GetUnifiedByUserAsync(owner, 1, 10, ct: ct)).Items);
        Assert.Empty((await queries.GetPublicAsync(null, Guid.NewGuid(), null, null, 10, null, ct)).Items);

        row.ChangeVisibility(CheckInVisibility.Private);
        await db.SaveChangesAsync(ct);
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, null, ct));
        Assert.Null(await queries.GetHomeByIdAsync(row.Id, null, ct));
        Assert.Null(await queries.GetModerationSnapshotAsync(row.Id, row.ContentRevision, ct));
        Assert.Empty((await queries.GetPublicAsync(null, null, null, null, 10, null, ct)).Items);
        Assert.NotNull(await queries.GetHomeByIdAsync(row.Id, owner, ct));
        row.Delete();
        await db.SaveChangesAsync(ct);
        Assert.Null(await queries.GetHomeByIdAsync(row.Id, owner, ct));
    }
}
