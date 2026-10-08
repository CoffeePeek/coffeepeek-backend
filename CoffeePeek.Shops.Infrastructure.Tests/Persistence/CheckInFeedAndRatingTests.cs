using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class CheckInFeedAndRatingTests
{
    private static CheckIn Approved(Guid user,Guid shop,int score,DateTime visited,DateTime created,Guid? id=null)
    {
        var c=CheckIn.CreateUnified(user,shop,"author","x",Rating.Create(score,score,score),visited,CheckInVisibility.Public,id:id);
        c.CreatedAtUtc=created;
        c.ApplyModerationDecision(1,Guid.NewGuid(),1,CheckInModerationState.Approved,null,created);
        return c;
    }

    [Fact]
    public async Task LatestEligibleRatingsFallbackWhileFeedKeepsAllVisits()
    {
        await using var db=new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var shop=new CoffeeShop(null,"shop",null,PriceRange.Moderate,Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(),"address",53m,27m);
        db.Shops.Add(shop);
        var user=Guid.NewGuid(); var time=DateTime.UtcNow.AddDays(-1);
        var old=Approved(user,shop.Id,2,time,time);
        var latest=Approved(user,shop.Id,5,time.AddHours(1),time.AddHours(1));
        var backdated=Approved(user,shop.Id,1,time.AddHours(-1),time.AddHours(2));
        var other=Approved(Guid.NewGuid(),shop.Id,3,time,time);
        var pending=CheckIn.CreateUnified(Guid.NewGuid(),shop.Id,"pending","x",Rating.Create(1,1,1),time,CheckInVisibility.Public);
        db.CheckIns.AddRange(old,latest,backdated,other,pending); await db.SaveChangesAsync();
        var ratings=new CheckInQueries(db);
        Assert.Equal((4m,4),await ratings.GetStatsByShopAsync(shop.Id,default));
        var queries=new CheckInQueries(db);
        var first=await queries.GetPublicAsync(null,null,shop.Id,null,2,null);
        Assert.Equal(2,first.Items.Length); Assert.NotNull(first.NextCursor);
        var second=await queries.GetPublicAsync(null,null,shop.Id,null,2,first.NextCursor);
        Assert.Empty(first.Items.Select(c=>c.Id).Intersect(second.Items.Select(c=>c.Id)));
        latest.ChangeVisibility(CheckInVisibility.Private); await db.SaveChangesAsync();
        Assert.Equal((2.5m,3),await ratings.GetStatsByShopAsync(shop.Id,default));
        old.Delete(); await db.SaveChangesAsync();
        Assert.Equal((2m,2),await ratings.GetStatsByShopAsync(shop.Id,default));
        backdated.ApplyModerationDecision(1,backdated.ModerationSubmissionId!.Value,2,CheckInModerationState.Rejected,"reason",time);
        await db.SaveChangesAsync();
        Assert.Equal((3m,1),await ratings.GetStatsByShopAsync(shop.Id,default));
        Assert.Equal(1,(await new PublicStatsQueryRepository(db).GetStatsAsync()).TotalCheckIns);
    }

    [Fact]
    public void RatingAndCursorQueriesTranslateToPostgres()
    {
        using var db=new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql("Host=localhost;Database=test;Username=test;Password=test").Options);
        var id=Guid.NewGuid();var time=DateTime.UtcNow;
        var sql=CheckInEligibility.LatestRatings(db).Where(c=>c.CreatedAtUtc<time || (c.CreatedAtUtc==time && c.Id.CompareTo(id)<0)).ToQueryString();
        Assert.Contains("NOT EXISTS",sql);Assert.Contains("VisitedAt",sql);Assert.Contains("ORDER",CheckInEligibility.Public(db).OrderByDescending(c=>c.CreatedAtUtc).ThenByDescending(c=>c.Id).ToQueryString());
        Assert.Equal((time,id),CheckInQueries.DecodeCursor(CheckInQueries.EncodeCursor(time,id)));
        Assert.Throws<CoffeePeek.Shared.Kernel.Exceptions.ValidationException>(()=>CheckInQueries.DecodeCursor("bad"));
    }

    [Fact]
    public async Task ConcurrentApprovalCannotOverwriteCommittedAuthorHide()
    {
        var options=new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var first=new ShopsDbContext(options);await using var second=new ShopsDbContext(options);
        var row=Approved(Guid.NewGuid(),Guid.NewGuid(),4,DateTime.UtcNow,DateTime.UtcNow);
        first.CheckIns.Add(row);await first.SaveChangesAsync();
        var stale=await second.CheckIns.SingleAsync();
        row.ChangeVisibility(CheckInVisibility.Private);await first.SaveChangesAsync();
        stale.ApplyModerationDecision(1,stale.ModerationSubmissionId!.Value,2,CheckInModerationState.Approved,null,DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>second.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductionAuditInterceptorPreservesHistoricalCreationAndVisitTime()
    {
        var created=DateTime.UtcNow.AddYears(-1);var visit=created.AddDays(-1);
        await using var db=new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new CheckInAuditInterceptor()).Options);
        var row=CheckIn.CreateUnified(Guid.NewGuid(),Guid.NewGuid(),"author","visit",Rating.Create(3,4,5),visit);
        row.CreatedAtUtc=created;db.CheckIns.Add(row);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var stored=await db.CheckIns.SingleAsync();Assert.Equal(created,stored.CreatedAtUtc);Assert.Equal(visit,stored.VisitedAt);
    }
}
