using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class CheckInQueriesTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static DateTime Utc(int month, int day) => new(2026, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static CheckIn Create(Guid user, Guid shop, DateTime visited) =>
        CheckIn.CreateUnified(user, shop, "author", "visit", CoffeePeek.Shops.Domain.Entities.Rating.Create(4,4,4), visited);

    [Fact]
    public async Task GetByUserId_WithRange_ReturnsOnlyUsersCheckInsInHalfOpenRange()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        var onFrom = Create(userId, shopId, Utc(9, 1));
        var inside = Create(userId, shopId, Utc(9, 15));
        var onTo = Create(userId, shopId, Utc(10, 1));
        var before = Create(userId, shopId, Utc(8, 31));
        var otherUser = Create(Guid.NewGuid(), shopId, Utc(9, 10));
        db.CheckIns.AddRange(onFrom, inside, onTo, before, otherUser);
        await db.SaveChangesAsync(_ct);

        var sut = new CheckInQueries(db);

        var (items, total) = await sut.GetUnifiedByUserAsync(userId, 1, 100, Utc(9, 1), Utc(10, 1), _ct);

        total.Should().Be(2);
        items.Select(i => i.Id).Should().BeEquivalentTo([onFrom.Id, inside.Id]);
    }

    [Fact]
    public async Task GetByUserId_PreservesConsumedDrinkAndUsesProtectedPhotoEndpoint()
    {
        // Goes through ProjectToType, so it proves the Urls map is translatable by the EF pipeline.
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var userId = Guid.NewGuid();

        var checkIn = Create(userId, Guid.NewGuid(), Utc(9, 15));
        checkIn.SetDrink(new("other", "Эспрессо-тоник", "Эспрессо-тоник", "Espresso tonic"));
        checkIn.AddPhotos([new ShopPhoto("p.jpg", "image/jpeg", "checkins/p.jpg", 1024, userId)]);
        db.CheckIns.Add(checkIn);
        await db.SaveChangesAsync(_ct);

        var sut = new CheckInQueries(db);

        var (items, _) = await sut.GetUnifiedByUserAsync(userId, 1, 100, null, null, _ct);

        var projected = items.Should().ContainSingle().Subject;
        projected.Drink!.Slug.Should().Be("other");
        projected.Drink!.CustomName.Should().Be("Эспрессо-тоник");
        projected.Drink!.NameRu.Should().Be("Эспрессо-тоник");
        projected.Drink!.NameEn.Should().Be("Espresso tonic");
        var photo = projected.Photos.Should().ContainSingle().Subject;
        photo.Id.Should().Be(checkIn.ShopPhotos.Single().Id);
        photo.Url.Should().Be($"/api/v1/check-ins/{checkIn.Id}/photos/{photo.Id}");
    }
}
