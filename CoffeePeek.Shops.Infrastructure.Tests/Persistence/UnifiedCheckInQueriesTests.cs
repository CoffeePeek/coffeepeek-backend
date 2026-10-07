using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class UnifiedCheckInQueriesTests
{
    [Fact]
    public async Task PrivateForeignAndDeletedEntriesAreNotReadable()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var row = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", "x", Rating.Create(1, 2, 3), DateTime.UtcNow);
        db.CheckIns.Add(row);
        await db.SaveChangesAsync();
        var queries = new CheckInQueries(db);
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, Guid.NewGuid()));
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, null));
        Assert.Equal(row.Id, (await queries.GetUnifiedByIdAsync(row.Id, row.UserId))!.Id);
        row.Delete();
        await db.SaveChangesAsync();
        Assert.Null(await queries.GetUnifiedByIdAsync(row.Id, row.UserId));
        Assert.Empty((await queries.GetUnifiedByUserAsync(row.UserId, 1, 10)).Items);
    }

    [Fact]
    public void ModelKeepsRowVersionSeparateAndPublicSqlIsTranslatable()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=disposable;Username=test;Password=test").Options);
        var entity = db.Model.FindEntityType(typeof(CheckIn))!;
        Assert.True(entity.FindProperty(nameof(CheckIn.RowVersion))!.IsConcurrencyToken);
        Assert.False(entity.FindProperty(nameof(CheckIn.ContentRevision))!.IsConcurrencyToken);
        var sql = CheckInEligibility.LatestRatings(db).ToQueryString();
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("ApprovedRevision", sql);
        Assert.Contains("VisitedAt", sql);
    }
}
