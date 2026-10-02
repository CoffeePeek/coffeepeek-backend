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

    [Fact]
    public async Task GetByUserId_ProjectsPhotoVariantUrls()
    {
        // Goes through ProjectToType, so it proves the Urls map is translatable by the EF pipeline.
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var userId = Guid.NewGuid();

        var checkIn = CheckIn.Create(userId, Guid.NewGuid(), Utc(9, 15));
        checkIn.SetDrink(new("other", "Эспрессо-тоник", "Эспрессо-тоник", "Espresso tonic"));
        checkIn.AddPhotos([new ShopPhoto("p.jpg", "image/jpeg", "checkins/p.jpg", 1024, userId)]);
        db.CheckIns.Add(checkIn);
        await db.SaveChangesAsync(_ct);

        var sut = new CheckInQueries(db, MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.coffeepeek.by",
            ImageProxyEndpoint = "https://media.coffeepeek.by/img"
        }));

        var (items, _) = await sut.GetByUserId(userId, 1, 100, null, null, _ct);

        var projected = items.Should().ContainSingle().Subject;
        projected.DrinkSlug.Should().Be("other");
        projected.CustomDrinkName.Should().Be("Эспрессо-тоник");
        projected.DrinkNameRu.Should().Be("Эспрессо-тоник");
        projected.DrinkNameEn.Should().Be("Espresso tonic");
        var photo = projected.Photos.Should().ContainSingle().Subject;
        photo.FullUrl.Should().Be("https://media.coffeepeek.by/coffeepeek.shops/checkins/p.jpg");
        photo.Urls.Should().NotBeNull();
        photo.Urls!.Thumbnail.Should().Be("https://media.coffeepeek.by/img/_/thumb/plain/coffeepeek.shops/checkins/p.jpg");
        photo.Urls.Card.Should().Be("https://media.coffeepeek.by/img/_/card/plain/coffeepeek.shops/checkins/p.jpg");
        photo.Urls.Detail.Should().Be("https://media.coffeepeek.by/img/_/detail/plain/coffeepeek.shops/checkins/p.jpg");
        photo.Urls.Fullscreen.Should().Be("https://media.coffeepeek.by/img/_/full/plain/coffeepeek.shops/checkins/p.jpg");
    }
}
