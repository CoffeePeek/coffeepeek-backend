using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using CoffeePeek.Shops.Persistance.Modules;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class MenuOcrJobPersistenceTests
{
    [Fact]
    public async Task ProductionMenuScope_AppliesCompleteStoredOfferToTrackedMenu()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var services = new ServiceCollection();
        services.AddScoped(_ => new ShopsDbContext(options));
        services.AddMenuModule();
        services.AddSingleton(Mock.Of<IDbContextOutbox>());
        services.AddSingleton(Mock.Of<ICoffeeShopRepository>());
        services.AddSingleton(Mock.Of<IShopPriceRangeWriter>());
        using var provider = services.BuildServiceProvider();
        var menu = ShopMenu.Create(Guid.NewGuid());
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/first", 3);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        job.Start(DateTime.UtcNow);
        job.StoreResult("{\"success\":true,\"items\":[{\"slug\":\"espresso\",\"price\":8,\"volumeMl\":200,\"rawName\":\"espresso\"}],\"unmatched\":[]}", DateTime.UtcNow);
        await using (var seed = new ShopsDbContext(options))
        {
            seed.CoffeeDrinkDefinitions.Add(CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "espresso", "Эспрессо", "Espresso", CoffeeDrinkCategory.Espresso, "espresso", 1));
            seed.ShopMenus.Add(menu); await seed.SaveChangesAsync(ct);
        }
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>().ApplyStoredAsync(job.Id, null, ct);
        await using var verify = new ShopsDbContext(options);
        Assert.Equal(MenuOcrJobStatus.Applied, (await verify.MenuOcrJobs.SingleAsync(ct)).Status);
        Assert.Equal(8, Assert.Single((await verify.ShopMenus.Include(m => m.Items).SingleAsync(ct)).Items).Price);
    }

    [Fact]
    public async Task ResultSurvivesFreshContextForLaterApplyRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var menu = ShopMenu.Create(Guid.NewGuid());
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/first", 100);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        await using (var writer = new ShopsDbContext(options))
        {
            writer.ShopMenus.Add(menu);
            await writer.SaveChangesAsync(ct);
        }

        await using (var resultWriter = new ShopsDbContext(options))
        {
            var stored = await resultWriter.MenuOcrJobs.SingleAsync(j => j.Id == job.Id, ct);
            stored.Start(DateTime.UtcNow);
            stored.StoreResult("{\"items\":[]}", DateTime.UtcNow);
            await resultWriter.SaveChangesAsync(ct);
        }

        await using (var retry = new ShopsDbContext(options))
        {
            var stored = await retry.MenuOcrJobs.SingleAsync(j => j.Id == job.Id, ct);
            Assert.Equal(MenuOcrJobStatus.ResultStored, stored.Status);
            Assert.Equal("{\"items\":[]}", stored.ResultJson);
            Assert.Equal(menu.Id, stored.ShopMenuId);
        }
    }
}
