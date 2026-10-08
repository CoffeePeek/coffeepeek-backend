using System.Text.Json;
using System.Text.Json.Serialization;
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
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wolverine;
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
        var handler = CreateHandler(db, cache.Object);
        var message = Message(shop.Id, ShopChangeSection.Description,
            new ShopChangePayloadDto(Description: "New description"));

        await handler.Handle(message, TestContext.Current.CancellationToken);
        await handler.Handle(message, TestContext.Current.CancellationToken);

        shop.Description.Should().Be("New description");
        shop.ProfileRevision.Should().Be(1);
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

        var handler = CreateHandler(db);

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
                [new UploadedPhotoDto("menu.jpg", "image/jpeg", "menus/new", 50)],
                ExpectedInputRevision: 0))),
            TestContext.Current.CancellationToken);

        var menu = await db.ShopMenus.Include(x => x.Items).Include(x => x.Photos)
            .SingleAsync(x => x.CoffeeShopId == shop.Id, TestContext.Current.CancellationToken);
        menu.Items.Should().ContainSingle(x => x.DrinkDefinitionId == drink.Id && x.Price == 5);
        menu.Photos.Should().ContainSingle(x => x.StorageKey == "menus/new");
        menu.CapturedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_MenuRevisionChangedAfterSubmission_ReportsFailureWithoutPartialWrite()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Guid shopId;
        await using (var seed = new ShopsDbContext(options))
        {
            var shop = CreateShop();
            var drink = CoffeeDrinkDefinition.CreateWithId(
                Guid.NewGuid(), "espresso", "Эспрессо", "Espresso",
                DomainCoffeeDrinkCategory.Espresso, "", 1);
            shopId = shop.Id;
            seed.AddRange(shop, drink, ShopMenu.Create(shopId));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var pending = Message(shopId, ShopChangeSection.Menu,
            new ShopChangePayloadDto(Menu: new(
                [new UpdateShopMenuItemRequest("espresso", ContractAvailability.Present, 5, 30)],
                [],
                [new UploadedPhotoDto("old.jpg", "image/jpeg", "menus/old", 50)],
                ExpectedInputRevision: 0)));

        await using (var manual = new ShopsDbContext(options))
        {
            var menu = await manual.ShopMenus.SingleAsync(x => x.CoffeeShopId == shopId,
                TestContext.Current.CancellationToken);
            menu.MarkManuallyCaptured(DateTime.UtcNow);
            await manual.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var apply = new ShopsDbContext(options))
        {
            var failed = await CreateHandler(apply).Handle(pending, TestContext.Current.CancellationToken);
            failed.Should().NotBeNull();
            failed!.RequestId.Should().Be(pending.RequestId);
            failed.Reason.Should().Contain("revision");
        }

        await using var verify = new ShopsDbContext(options);
        var current = await verify.ShopMenus.Include(x => x.Items).Include(x => x.Photos)
            .SingleAsync(x => x.CoffeeShopId == shopId, TestContext.Current.CancellationToken);
        current.Items.Should().BeEmpty();
        current.Photos.Should().BeEmpty();
        (await verify.AppliedShopChangeRequests.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_MenuReplayFromFreshContext_DoesNotDuplicateItemsPhotosOrMarker()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Guid shopId;
        Guid originalItemId;
        await using (var seed = new ShopsDbContext(options))
        {
            var shop = CreateShop();
            var drink = CoffeeDrinkDefinition.CreateWithId(
                Guid.NewGuid(), "espresso", "Эспрессо", "Espresso",
                DomainCoffeeDrinkCategory.Espresso, "", 1);
            shopId = shop.Id;
            var seededMenu = ShopMenu.Create(shopId);
            seededMenu.ApplyManualItem(drink.Id,
                CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability.Present,
                3, 20, Guid.NewGuid());
            originalItemId = seededMenu.Items.Single().Id;
            seed.AddRange(shop, drink, seededMenu);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var approved = Message(shopId, ShopChangeSection.Menu,
            new ShopChangePayloadDto(Menu: new(
                [new UpdateShopMenuItemRequest("espresso", ContractAvailability.Present, 5, 30)],
                [],
                [new UploadedPhotoDto("menu.jpg", "image/jpeg", "menus/new", 50)],
                ExpectedInputRevision: 1)));
        await using (var first = new ShopsDbContext(options))
            (await CreateHandler(first).Handle(approved, TestContext.Current.CancellationToken)).Should().BeNull();
        await using (var replay = new ShopsDbContext(options))
            (await CreateHandler(replay).Handle(approved, TestContext.Current.CancellationToken)).Should().BeNull();

        await using var verify = new ShopsDbContext(options);
        var menu = await verify.ShopMenus.Include(x => x.Items).Include(x => x.Photos)
            .SingleAsync(x => x.CoffeeShopId == shopId, TestContext.Current.CancellationToken);
        menu.Items.Should().ContainSingle(x => x.Id == originalItemId && x.Price == 5 && x.VolumeMl == 30);
        menu.Photos.Should().ContainSingle(x => x.StorageKey == "menus/new");
        (await verify.AppliedShopChangeRequests.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_LegacyMenuEventWithoutRevision_RequiresReconciliation()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        db.Shops.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var original = Message(shop.Id, ShopChangeSection.Menu,
            new ShopChangePayloadDto(Menu: new(null!, null!, null!)));
        var legacyJson = JsonSerializer.Serialize(original, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        var legacy = JsonSerializer.Deserialize<ShopChangeRequestApprovedV1>(legacyJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var failed = await CreateHandler(db).Handle(legacy, TestContext.Current.CancellationToken);

        failed.Should().NotBeNull();
        failed!.Reason.Should().Contain("revision");
        (await db.ShopMenus.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.AppliedShopChangeRequests.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_ShopMissing_ReportsFailureInsteadOfThrowing()
    {
        // Regression: the NotFoundException dead-lettered the message and Moderation kept showing Approved.
        await using var db = CreateDbContext();
        var message = Message(Guid.NewGuid(), ShopChangeSection.Description,
            new ShopChangePayloadDto(Description: "New description"));

        var failed = await CreateHandler(db).Handle(message, TestContext.Current.CancellationToken);

        failed.Should().NotBeNull();
        failed!.RequestId.Should().Be(message.RequestId);
        (await db.AppliedShopChangeRequests.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_InvalidPayload_ReportsFailureAndDoesNotApply()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        db.Shops.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var failed = await CreateHandler(db).Handle(
            Message(shop.Id, ShopChangeSection.Contacts, new ShopChangePayloadDto()),
            TestContext.Current.CancellationToken);

        failed.Should().NotBeNull();
        db.ChangeTracker.Entries().Should().BeEmpty("partial changes must not be committed");
    }

    [Fact]
    public async Task Handle_GalleryForeignUpload_ReportsFailureWithoutChangingShop()
    {
        await using var db = CreateDbContext();
        var shop = CreateShop();
        db.Shops.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var lookup = new Mock<IUploadedPhotoLookup>();
        lookup.Setup(p => p.ClaimForShopAsync(It.IsAny<Guid>(), shop.Id,
                ShopPhotoAttachmentKind.Gallery, It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShopPhotoClaimResult.Failed);

        var failure = await CreateHandler(db, photoLookup: lookup.Object).Handle(
            Message(shop.Id, ShopChangeSection.Photos,
                new ShopChangePayloadDto(Photos: new([], [
                    new UploadedPhotoDto("foreign.jpg", "image/jpeg", "shops/foreign", 12)]))),
            TestContext.Current.CancellationToken);

        failure.Should().NotBeNull();
        (await db.ShopPhotos.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.AppliedShopChangeRequests.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
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

    private static ShopChangeRequestApprovedHandler CreateHandler(ShopsDbContext db,
        ICacheService? cache = null, IUploadedPhotoLookup? photoLookup = null)
    {
        cache ??= CreateCache().Object;
        photoLookup ??= CreatePhotoLookup();
        var bus = new Mock<IMessageBus>().Object;
        return new ShopChangeRequestApprovedHandler(db, cache,
            NullLogger<ShopChangeRequestApprovedHandler>.Instance,
            new ApplyApprovedShopMenuChangeService(db, cache, photoLookup, bus),
            photoLookup, bus);
    }

    private static IUploadedPhotoLookup CreatePhotoLookup()
    {
        var mock = new Mock<IUploadedPhotoLookup>();
        mock.Setup(p => p.ClaimForShopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<ShopPhotoAttachmentKind>(), It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, ShopPhotoAttachmentKind _, IReadOnlyCollection<string> keys,
                CancellationToken _) => new ShopPhotoClaimResult(true,
                keys.Select(key => new ClaimedShopPhotoDto(Guid.NewGuid(), key,
                    "trusted.jpg", "image/jpeg", 64)).ToArray()));
        return mock.Object;
    }

    private static ShopChangeRequestApprovedV1 Message(
        Guid shopId,
        ShopChangeSection section,
        ShopChangePayloadDto payload) => new(
        Guid.NewGuid(), shopId, Guid.NewGuid(), section, payload,
        Guid.NewGuid(), DateTime.UtcNow);
}
