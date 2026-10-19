using System.Text.Json;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class MenuOcrJobLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public MenuOcrJobLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LockedApply_UsesStoredResultOnlyWhenMenuRevisionAndPhotosMatch(int mutationKind)
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("ocr-city-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("ocr-shop-" + Guid.NewGuid().ToString("N"), city.Id);
        var menu = ShopMenu.Create(shop.Id);
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/ocr-" + Guid.NewGuid().ToString("N"), 100);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        await using (var seed = fixture.Context())
        {
            seed.Cities.Add(city);
            seed.Shops.Add(shop);
            seed.ShopMenus.Add(menu);
            await seed.SaveChangesAsync(ct);
        }

        var apply = new Mock<IApplyShopMenuService>(MockBehavior.Strict);
        if (mutationKind == 0)
            apply.Setup(a => a.ApplyParseResultAsync(
                    shop.Id, It.IsAny<IReadOnlyList<ParsedMenuItemDto>>(),
                    It.IsAny<IReadOnlyList<UnmatchedMenuItemDto>>(),
                    It.IsAny<CoffeePeek.Contract.Enums.PriceRange?>(),
                    It.IsAny<IReadOnlyList<ShopMenuPhotoSnapshot>>(),
                    It.IsAny<DateTime>(), It.IsAny<Guid?>(), ct))
                .Returns(Task.CompletedTask);

        await using (var claim = fixture.Context())
            Assert.True(await new MenuOcrJobStore(claim, apply.Object).TryStartCurrentAsync(job.Id, ct));

        var result = new ParseMenuPhotosResponse(true, null, null, [], []);
        await using (var persist = fixture.Context())
            Assert.True(await new MenuOcrJobStore(persist, apply.Object).StoreResultAsync(
                job.Id, JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ct));

        if (mutationKind == 1)
        {
            await using var edit = fixture.Context();
            var current = await edit.ShopMenus.Include(m => m.Photos)
                .SingleAsync(m => m.Id == menu.Id, ct);
            Assert.True(current.RemovePhotos([photo.Id]).IsSuccess);
            await edit.SaveChangesAsync(ct);
        }
        else if (mutationKind == 2)
        {
            await using var edit = fixture.Context();
            var drinkId = await edit.CoffeeDrinkDefinitions.Select(d => d.Id).FirstAsync(ct);
            await new ShopMenuRepository(edit).ApplyManualItemsAsync(shop.Id,
                [new ManualShopMenuItemUpdate(drinkId, MenuItemAvailability.Present, 5m)],
                Guid.NewGuid(), ct);
            await edit.SaveChangesAsync(ct);
        }

        await using (var applyContext = fixture.Context())
            await new MenuOcrJobStore(applyContext, apply.Object).ApplyStoredAsync(job.Id, null, ct);

        await using var verify = fixture.Context();
        var persisted = await verify.MenuOcrJobs.SingleAsync(j => j.Id == job.Id, ct);
        Assert.Equal(mutationKind == 0 ? MenuOcrJobStatus.Applied : MenuOcrJobStatus.Stale, persisted.Status);
        apply.Verify(a => a.ApplyParseResultAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ParsedMenuItemDto>>(),
            It.IsAny<IReadOnlyList<UnmatchedMenuItemDto>>(),
            It.IsAny<CoffeePeek.Contract.Enums.PriceRange?>(),
            It.IsAny<IReadOnlyList<ShopMenuPhotoSnapshot>>(), It.IsAny<DateTime>(),
            It.IsAny<Guid?>(), ct), mutationKind == 0 ? Times.Once : Times.Never);
    }

    [Fact]
    public async Task OperatorRecovery_LocksRunningJobAndRejectsLateProviderResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("ocr-recovery-city-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("ocr-recovery-shop-" + Guid.NewGuid().ToString("N"), city.Id);
        var menu = ShopMenu.Create(shop.Id);
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/ocr-" + Guid.NewGuid().ToString("N"), 100);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        await using (var seed = fixture.Context())
        {
            seed.Cities.Add(city);
            seed.Shops.Add(shop);
            seed.ShopMenus.Add(menu);
            await seed.SaveChangesAsync(ct);
        }

        var apply = new Mock<IApplyShopMenuService>(MockBehavior.Strict);
        await using (var start = fixture.Context())
            Assert.True(await new MenuOcrJobStore(start, apply.Object).TryStartCurrentAsync(job.Id, ct));
        await using (var recover = fixture.Context())
            Assert.Equal(MenuOcrJobStatus.Failed, await new MenuOcrJobStore(recover, apply.Object)
                .RecoverUnknownOutcomeAsync(job.Id,
                    DateTime.UtcNow.Add(MenuOcrJobStore.OperatorRecoveryMinimumAge).AddMinutes(1), ct));
        await using (var late = fixture.Context())
            Assert.False(await new MenuOcrJobStore(late, apply.Object)
                .StoreResultAsync(job.Id, "{\"success\":true}", ct));

        await using var verify = fixture.Context();
        var persisted = await verify.MenuOcrJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, ct);
        Assert.Equal(MenuOcrJobStatus.Failed, persisted.Status);
        Assert.Equal(MenuOcrJob.ProviderOutcomeUnknownError, persisted.Error);
        Assert.Null(persisted.ResultJson);
        var persistedMenu = await verify.ShopMenus.AsNoTracking().SingleAsync(m => m.Id == menu.Id, ct);
        Assert.Equal(MenuParseStatus.Failed, persistedMenu.ParseStatus);
        apply.VerifyNoOtherCalls();
    }
}
