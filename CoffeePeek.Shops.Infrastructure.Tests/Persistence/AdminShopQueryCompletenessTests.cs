using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class AdminShopQueryCompletenessTests
{
    private static readonly MediaPublicUrlOptions Media = new()
    {
        PublicEndpoint = "https://media.example.test",
        ShopBucketName = "shops"
    };

    [Fact]
    public async Task AdminAndOwnerLists_LoadPublishedCollectionsFromFreshContexts_WithoutChangingPagination()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var ownerId = Guid.NewGuid();
        var otherOwnerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        CoffeeShop older;
        CoffeeShop newer;
        CoffeeShop other;

        await using (var seed = new ShopsDbContext(options))
        {
            older = AddCompleteShop(seed, "owner older", ownerId, now.AddMinutes(-2));
            newer = AddCompleteShop(seed, "owner newer", ownerId, now.AddMinutes(-1));
            other = AddCompleteShop(seed, "other owner", otherOwnerId, now);
            await seed.SaveChangesAsync(ct);
        }

        // Each query uses a new context: relationship fixup from seeding cannot fill a missing Include.
        await using (var adminDb = new ShopsDbContext(options))
        {
            var repository = new AdminCoffeeShopQueryRepository(adminDb);
            var (firstPage, totalCount) = await repository.GetPagedAsync(1, 1, null, null, ct);
            Assert.Equal(3, totalCount);
            Assert.Equal(other.Id, Assert.Single(firstPage).Id);
            AssertComplete(other, Assert.Single(firstPage));

            var (secondPage, secondTotal) = await repository.GetPagedAsync(2, 1, null, null, ct);
            Assert.Equal(3, secondTotal);
            Assert.Equal(newer.Id, Assert.Single(secondPage).Id);
            AssertComplete(newer, Assert.Single(secondPage));

            var (searched, searchTotal) = await repository.GetPagedAsync(1, 10, "owner newer", null, ct);
            Assert.Equal(1, searchTotal);
            Assert.Equal(newer.Id, Assert.Single(searched).Id);
            AssertComplete(newer, Assert.Single(searched));
        }

        await using (var ownerDb = new ShopsDbContext(options))
        {
            var repository = new AdminCoffeeShopQueryRepository(ownerDb);
            var owned = await repository.GetByOwnerUserIdAsync(ownerId, ct);
            Assert.Equal(2, owned.Count);
            var expectedOwnerOrder = new[] { older, newer }.OrderBy(s => s.Name).ToArray();
            Assert.Equal(expectedOwnerOrder.Select(s => s.Id), owned.Select(s => s.Id));
            Assert.All(owned, s => Assert.Equal(ownerId, s.OwnerUserId));
            for (var i = 0; i < owned.Count; i++)
                AssertComplete(expectedOwnerOrder[i], owned[i]);
        }
    }

    private static CoffeeShop AddCompleteShop(ShopsDbContext db, string name, Guid ownerId, DateTime createdAtUtc)
    {
        var shop = new CoffeeShop(null, name, null, PriceRange.Moderate, Guid.NewGuid())
        {
            CreatedAtUtc = createdAtUtc
        };
        shop.AssignOwner(ownerId);
        shop.SetLocation(Guid.NewGuid(), name + " address", 53m, 27m);
        shop.AddPhotos([
            new ShopPhoto(name + "-1.jpg", "image/jpeg", "shops/" + name + "/1", 10, ownerId),
            new ShopPhoto(name + "-2.jpg", "image/jpeg", "shops/" + name + "/2", 20, ownerId)
        ]);

        var category = new EquipmentCategory();
        db.Entry(category).Property(x => x.Name).CurrentValue = name + " category";
        shop.AddEquipment(new Equipment(name, "model", category));
        shop.SetBeans([new CoffeeBean(name + " bean")]);
        shop.SetRoasters([new Roaster(name + " roaster")]);
        shop.SetBrewMethods([new BrewMethod(name + " brew", BrewMethodCategory.Gravity)]);
        var tag = ShopTag.Create(name.Replace(' ', '_'), name + " tag");
        db.ShopTags.Add(tag);
        shop.SetTags([tag.Id], ownerId);
        db.Shops.Add(shop);
        return shop;
    }

    private static void AssertComplete(CoffeeShop expected, CoffeeShop actual)
    {
        var dto = AdminPublishedShopMapper.Map(actual, Media);
        Assert.Equal(expected.Id, dto.Id);
        Assert.Equal(expected.ShopPhotos.Select(p => p.Id).Order(), dto.Photos.Select(p => p.Id).Order());
        Assert.Equal(expected.Equipments.Select(e => e.Id), dto.EquipmentIds);
        Assert.Equal(expected.CoffeeBeans.Select(b => b.Id), dto.BeanIds);
        Assert.Equal(expected.Roasters.Select(r => r.Id), dto.RoasterIds);
        Assert.Equal(expected.BrewMethods.Select(b => b.Id), dto.BrewMethodIds);
        Assert.Equal(expected.ShopTags.Select(t => t.TagId), dto.TagIds);
        Assert.Equal(2, dto.Photos.Count);
        Assert.Single(dto.EquipmentIds!);
        Assert.Single(dto.BeanIds!);
        Assert.Single(dto.RoasterIds!);
        Assert.Single(dto.BrewMethodIds!);
        Assert.Single(dto.TagIds!);
    }
}
