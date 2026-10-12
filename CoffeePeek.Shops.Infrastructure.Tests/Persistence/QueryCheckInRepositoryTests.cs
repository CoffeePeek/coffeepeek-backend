using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class QueryCheckInRepositoryTests
{
    [Theory]
    [InlineData(true, true, 1, false, true)]
    [InlineData(true, false, 1, false, false)]
    [InlineData(false, true, 1, false, false)]
    [InlineData(true, true, 4, false, false)]
    [InlineData(true, true, 3, false, true)]
    [InlineData(true, true, 1, true, true)]
    public async Task ExistsSinceAsyncScopesCooldownToUserAndShop(
        bool sameUser, bool sameShop, int hoursAgo, bool deleted, bool expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var row = CheckIn.CreateUnified(sameUser ? userId : Guid.NewGuid(), sameShop ? shopId : Guid.NewGuid(),
            "author", "visit", Rating.Create(4, 4, 4), now.AddDays(-1));
        row.CreatedAtUtc = now.AddHours(-hoursAgo);
        if (deleted) row.Delete();
        db.CheckIns.Add(row);
        await db.SaveChangesAsync(ct);

        var repository = new QueryCheckInRepository(db);

        Assert.Equal(expected, await repository.ExistsSinceAsync(userId, shopId, now.AddHours(-3), ct));
    }
}
