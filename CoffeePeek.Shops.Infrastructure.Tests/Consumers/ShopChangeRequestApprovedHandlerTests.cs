using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContractAvailability = CoffeePeek.Contract.Enums.MenuItemAvailability;
using DomainCoffeeDrinkCategory = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.CoffeeDrinkCategory;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public class ShopChangeRequestApprovedHandlerTests
{
    [Fact]
    public async Task Handle_AppliesDescriptionAndIgnoresDuplicateDelivery()
    {
        await using var db = CreateDbContext();
        var shop = new CoffeeShop(
            Guid.NewGuid(), "Coffee", "Old", DomainPriceRange.Moderate, Guid.NewGuid());
        db.Shops.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var cache = CreateCache();
        var handler = new ShopChangeRequestApprovedHandler(
            db, cache.Object, NullLogger<ShopChangeRequestApprovedHandler>.Instance);
        var message = Message(shop.Id, ShopChangeSection.Description,
            new ShopChangePayloadDto(Description: "New description"));

        await handler.Handle(message, TestContext.Current.CancellationToken);
        await handler.Handle(message, TestContext.Current.CancellationToken);

        shop.Description.Should().Be("New description");
        (await db.AppliedShopChangeRequests.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_AppliesContacts()
    {
        await using var db = CreateDbContext();
        var shop = new CoffeeShop(
            Guid.NewGuid(), "Coffee", null, DomainPriceRange.Moderate, Guid.NewGuid());
        db.Shops.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ShopChangeRequestApprovedHandler(
            db, CreateCache().Object, NullLogger<ShopChangeRequestApprovedHandler>.Instance);

        await handler.Handle(Message(shop.Id, ShopChangeSection.Contacts,
            new ShopChangePayloadDto(Contacts: new("+375291234567", "hi@example.com", "https://cafe.test", "@cafe"))),
            TestContext.Current.CancellationToken);

        shop.Contact.PhoneNumber.Should().Be("+375291234567");
        shop.Contact.Email.Should().Be("hi@example.com");
        shop.Contact.SiteLink.Should().Be("https://cafe.test");
        shop.Contact.InstagramLink.Should().Be("@cafe");
    }

    [Fact]
    public async Task Handle_ReplacesAndReordersGalleryPhotos()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        var removed = new ShopPhoto("old.jpg", "image/jpeg", "shops/old", 10, Guid.NewGuid());
        var retained = new ShopPhoto("keep.jpg", "image/jpeg", "shops/keep", 20, Guid.NewGuid());
        shop.AddPhotos([removed, retained]);
        db.Shops.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await db.ShopPhotos.CountAsync(TestContext.Current.CancellationToken)).Should().Be(2);
        db.ChangeTracker.Clear();

        var handler = CreateHandler(db);
        await handler.Handle(Message(shop.Id, ShopChangeSection.Photos,
            new ShopChangePayloadDto(Photos: new(
                [retained.Id],
                [new UploadedPhotoDto("new.jpg", "image/jpeg", "shops/new", 30)]))),
            TestContext.Current.CancellationToken);

        var updated = await db.Shops.Include(x => x.ShopPhotos)
            .SingleAsync(x => x.Id == shop.Id, TestContext.Current.CancellationToken);
        updated.ShopPhotos.OrderBy(x => x.SortIndex).Select(x => x.StorageKey)
            .Should().Equal("shops/keep", "shops/new");
        updated.ShopPhotos.OrderBy(x => x.SortIndex).Select(x => x.SortIndex).Should().Equal(0, 1);
    }

    [Fact]
    public async Task Handle_AppliesTags()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        var tag = ShopTag.Create("quiet", "Quiet");
        db.AddRange(shop, tag);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateHandler(db).Handle(Message(shop.Id, ShopChangeSection.Tags,
            new ShopChangePayloadDto(TagIds: [tag.Id])), TestContext.Current.CancellationToken);

        shop.ShopTags.Select(x => x.TagId).Should().Equal(tag.Id);
    }

    [Fact]
    public async Task Handle_AppliesRoasters()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        var roaster = new Roaster("Coffee Circus");
        db.AddRange(shop, roaster);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateHandler(db).Handle(Message(shop.Id, ShopChangeSection.Roasters,
            new ShopChangePayloadDto(RoasterIds: [roaster.Id])), TestContext.Current.CancellationToken);

        shop.Roasters.Should().ContainSingle(x => x.Id == roaster.Id);
    }

    [Fact]
    public async Task Handle_AppliesEquipment()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        var category = new EquipmentCategory();
        typeof(EquipmentCategory).GetProperty(nameof(EquipmentCategory.Name))!
            .SetValue(category, "Espresso machines");
        var equipment = new Equipment("La Marzocco", "Linea", category);
        db.AddRange(shop, equipment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateHandler(db).Handle(Message(shop.Id, ShopChangeSection.Equipment,
            new ShopChangePayloadDto(EquipmentIds: [equipment.Id])), TestContext.Current.CancellationToken);

        shop.Equipments.Should().ContainSingle(x => x.Id == equipment.Id);
    }

    [Fact]
    public async Task Handle_AppliesBrewMethods()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        var method = new BrewMethod("V60", BrewMethodCategory.Gravity);
        db.AddRange(shop, method);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateHandler(db).Handle(Message(shop.Id, ShopChangeSection.BrewMethods,
            new ShopChangePayloadDto(BrewMethodIds: [method.Id])), TestContext.Current.CancellationToken);

        shop.BrewMethods.Should().ContainSingle(x => x.Id == method.Id);
    }

    [Fact]
    public async Task Handle_AppliesMenuItemsAndPhotos()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        var drink = CoffeeDrinkDefinition.CreateWithId(
            Guid.NewGuid(), "espresso", "Эспрессо", "Espresso",
            DomainCoffeeDrinkCategory.Espresso, "", 1);
        db.AddRange(shop, drink);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateHandler(db).Handle(Message(shop.Id, ShopChangeSection.Menu,
            new ShopChangePayloadDto(Menu: new(
                [new UpdateShopMenuItemRequest("espresso", ContractAvailability.Present, 5, 30)],
                [],
                [new UploadedPhotoDto("menu.jpg", "image/jpeg", "menus/new", 50)]))),
            TestContext.Current.CancellationToken);

        var menu = await db.ShopMenus.Include(x => x.Items).Include(x => x.Photos)
            .SingleAsync(x => x.CoffeeShopId == shop.Id, TestContext.Current.CancellationToken);
        menu.Items.Should().ContainSingle(x => x.DrinkDefinitionId == drink.Id && x.Price == 5);
        menu.Photos.Should().ContainSingle(x => x.StorageKey == "menus/new");
        menu.CapturedAtUtc.Should().NotBeNull();
    }

    private static ShopsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ShopsDbContext(options);
    }

    private static Mock<ICacheService> CreateCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.RemoveAsync(It.IsAny<CacheKey>())).Returns(Task.CompletedTask);
        cache.Setup(x => x.RemoveByPattern(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        return cache;
    }

    private static CoffeeShop CreateShop() => new(
        Guid.NewGuid(), "Coffee", null, DomainPriceRange.Moderate, Guid.NewGuid());

    private static ShopChangeRequestApprovedHandler CreateHandler(ShopsDbContext db) => new(
        db, CreateCache().Object, NullLogger<ShopChangeRequestApprovedHandler>.Instance);

    private static ShopChangeRequestApprovedV1 Message(
        Guid shopId,
        ShopChangeSection section,
        ShopChangePayloadDto payload) => new(
        Guid.NewGuid(), shopId, Guid.NewGuid(), section, payload,
        Guid.NewGuid(), DateTime.UtcNow);
}
