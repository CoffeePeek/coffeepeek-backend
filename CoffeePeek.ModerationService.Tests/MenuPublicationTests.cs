using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Contract.Responses;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Infrastructure.Consumers;
using CoffeePeek.Shared.Persistence.Data;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffeeShop.Moderation.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CoffeePeek.ModerationService.Tests;

public class MenuPublicationTests
{
    [Fact]
    public async Task ParseWhilePublicationIsInFlight_IsForwardedWhenShopIdArrives()
    {
        await using var db = NewDb();
        var shop = NewShop();
        shop.Approve();
        db.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var parsed = Parsed(shop);
        var parser = Parser(db);
        (await parser.Handle(parsed, TestContext.Current.CancellationToken)).Should().BeNull();

        var completion = new ModerationShopApproveCompleteHandler(new ModerationShopRepository(db),
            new UnitOfWork<ModerationDbContext>(db), NullLogger<ModerationShopApproveCompleteHandler>.Instance);
        var shopId = Guid.NewGuid();
        var reply = new ModerationShopApproveCompleteResponse(shop.Id, shopId);
        var outbound = await completion.Handle(reply, TestContext.Current.CancellationToken);

        outbound.Should().NotBeNull();
        outbound!.ShopId.Should().Be(shopId);
        outbound.Snapshot.Items.Should().ContainSingle().Which.Price.Should().Be(5);
        (await completion.Handle(reply, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ParseAfterManualEdit_IsIgnoredAndNotForwarded()
    {
        await using var db = NewDb();
        var shop = NewShop();
        var message = Parsed(shop);
        shop.ReplaceMenuItems([new() { Slug = "espresso", Availability = 1, Price = 9 }], DateTime.UtcNow);
        db.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        (await Parser(db).Handle(message, TestContext.Current.CancellationToken)).Should().BeNull();
        var saved = await new ModerationShopRepository(db).GetByIdAsync(shop.Id, TestContext.Current.CancellationToken);
        saved!.Menu!.Items.Should().ContainSingle().Which.Price.Should().Be(9);
    }

    [Fact]
    public async Task CorrelatedParseForPublishedShop_IsForwardedOnce()
    {
        await using var db = NewDb();
        var shop = NewShop();
        shop.Approve();
        shop.AddShopId(Guid.NewGuid());
        db.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var message = Parsed(shop);
        var parser = Parser(db);

        var outbound = await parser.Handle(message, TestContext.Current.CancellationToken);
        outbound!.ShopId.Should().Be(shop.ShopId);
        outbound.PreserveManualItems.Should().BeTrue();
        outbound.Snapshot.Items.Should().ContainSingle().Which.Price.Should().Be(5);
        (await parser.Handle(message, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    private static MenuParsedEvent Parsed(ModerationShop shop) => new(
        MenuParseSourceKind.ModerationShop, shop.Id, null, true, null, null,
        [new ParsedMenuItemDto("espresso", 5, 30, "Espresso")], [],
        DateTime.UtcNow, shop.Menu!.ParseRequestId);

    private static ModerationShop NewShop()
    {
        var shop = ModerationShop.Create("Cafe", null, Guid.NewGuid(), null);
        shop.SetLocation(new ModerationLocation("Address", 53, 27));
        shop.AttachMenuPhotos([("menu.jpg", "image/jpeg", "menus/a", 10)], DateTime.UtcNow);
        return shop;
    }

    private static MenuParsedEventHandler Parser(ModerationDbContext db) => new(
        new ShopImportCandidateRepository(db), new ModerationShopRepository(db),
        new UnitOfWork<ModerationDbContext>(db), NullLogger<MenuParsedEventHandler>.Instance);

    private static ModerationDbContext NewDb() => new(new DbContextOptionsBuilder<ModerationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
