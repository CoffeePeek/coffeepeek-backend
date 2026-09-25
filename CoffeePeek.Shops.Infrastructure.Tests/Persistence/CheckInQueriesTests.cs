using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class CheckInQueriesTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static DateTime Utc(int month, int day) => new(2026, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetByUserId_WithRange_ReturnsOnlyUsersCheckInsInHalfOpenRange()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        var onFrom = CheckIn.Create(userId, shopId, Utc(9, 1));
        var inside = CheckIn.Create(userId, shopId, Utc(9, 15));
        var onTo = CheckIn.Create(userId, shopId, Utc(10, 1));
        var before = CheckIn.Create(userId, shopId, Utc(8, 31));
        var otherUser = CheckIn.Create(Guid.NewGuid(), shopId, Utc(9, 10));
        db.CheckIns.AddRange(onFrom, inside, onTo, before, otherUser);
        await db.SaveChangesAsync(_ct);

        var sut = new CheckInQueries(db, MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions()));

        var (items, total) = await sut.GetByUserId(userId, 1, 100, Utc(9, 1), Utc(10, 1), _ct);

        total.Should().Be(2);
        items.Select(i => i.Id).Should().BeEquivalentTo([onFrom.Id, inside.Id]);
    }
}
