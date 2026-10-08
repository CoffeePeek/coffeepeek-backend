using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopChangeRequestMenuLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ShopChangeRequestMenuLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task VersionedApproval_ReplayAndStaleRequest_KeepOneAtomicMenuRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.City(
            "menu-change-city-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("menu-change-shop-" + Guid.NewGuid().ToString("N"), city.Id);
        string slug;
        Guid originalItemId;
        await using (var seed = fixture.Context())
        {
            var drink = await seed.CoffeeDrinkDefinitions.AsNoTracking()
                .Select(d => new { d.Id, d.Slug }).FirstAsync(ct);
            slug = drink.Slug;
            var seededMenu = ShopMenu.Create(shop.Id);
            seededMenu.ApplyManualItem(drink.Id, CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability.Present,
                3m, 100, Guid.NewGuid());
            originalItemId = seededMenu.Items.Single().Id;
            seed.Cities.Add(city);
            seed.Shops.Add(shop);
            seed.ShopMenus.Add(seededMenu);
            await seed.SaveChangesAsync(ct);
        }

        var first = Approved(shop.Id, slug, 1, "menus/approved-" + Guid.NewGuid().ToString("N"));
        var stale = Approved(shop.Id, slug, 1, "menus/stale-" + Guid.NewGuid().ToString("N"));

        await using (var apply = fixture.Context())
            Assert.Null(await Handler(apply).Handle(first, ct));
        await using (var replay = fixture.Context())
            Assert.Null(await Handler(replay).Handle(first, ct));
        await using (var reject = fixture.Context())
        {
            var failure = await Handler(reject).Handle(stale, ct);
            Assert.NotNull(failure);
            Assert.Equal(stale.RequestId, failure.RequestId);
            Assert.Contains("revision", failure.Reason, StringComparison.OrdinalIgnoreCase);
        }

        await using var verify = fixture.Context();
        var menu = await verify.ShopMenus.Include(m => m.Items).Include(m => m.Photos)
            .SingleAsync(m => m.CoffeeShopId == shop.Id, ct);
        Assert.Equal(originalItemId, Assert.Single(menu.Items).Id);
        Assert.Single(menu.Photos);
        Assert.Equal(first.Payload.Menu!.NewPhotos[0].StorageKey, menu.Photos.Single().StorageKey);
        Assert.Single(await verify.AppliedShopChangeRequests.Where(x => x.ShopId == shop.Id).ToListAsync(ct));
    }

    private static ShopChangeRequestApprovedV1 Approved(Guid shopId, string slug, long revision, string storageKey) =>
        new(Guid.NewGuid(), shopId, Guid.NewGuid(), ShopChangeSection.Menu,
            new ShopChangePayloadDto(Menu: new ShopChangeMenuDto(
                [new UpdateShopMenuItemRequest(slug, CoffeePeek.Contract.Enums.MenuItemAvailability.Present, 5m, 200)],
                [], [new UploadedPhotoDto("menu.jpg", "image/jpeg", storageKey, 100)], revision)),
            Guid.NewGuid(), DateTime.UtcNow);

    private static ShopChangeRequestApprovedHandler Handler(CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext db)
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.RemoveAsync(It.IsAny<CacheKey>())).Returns(Task.CompletedTask);
        cache.Setup(x => x.RemoveByPattern(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var photoLookup = new Mock<CoffeePeek.Shops.Application.Abstractions.IUploadedPhotoLookup>();
        photoLookup.Setup(p => p.ClaimForShopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                CoffeePeek.Contract.Dtos.ShopPhotos.ShopPhotoAttachmentKind.Menu,
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, CoffeePeek.Contract.Dtos.ShopPhotos.ShopPhotoAttachmentKind _,
                IReadOnlyCollection<string> keys, CancellationToken _) =>
                new CoffeePeek.Shops.Application.Abstractions.ShopPhotoClaimResult(true,
                    keys.Select(key => new CoffeePeek.Contract.Dtos.ShopPhotos.ClaimedShopPhotoDto(
                        Guid.NewGuid(), key, "trusted.jpg", "image/jpeg", 100)).ToArray()));
        var bus = new Mock<Wolverine.IMessageBus>();
        return new ShopChangeRequestApprovedHandler(db, cache.Object,
            NullLogger<ShopChangeRequestApprovedHandler>.Instance,
            new ApplyApprovedShopMenuChangeService(db, cache.Object, photoLookup.Object, bus.Object),
            photoLookup.Object, bus.Object);
    }
}
