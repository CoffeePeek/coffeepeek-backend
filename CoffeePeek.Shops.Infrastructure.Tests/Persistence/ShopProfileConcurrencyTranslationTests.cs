using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ShopProfileConcurrencyTranslationTests
{
    [Fact]
    public async Task StaleProfileRevision_BecomesNonRetryableConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var shop = new CoffeeShop(null, "Original", null, PriceRange.Cheap, Guid.NewGuid());

        await using (var seed = new ShopsDbContext(options))
        {
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(ct);
        }

        await using var adminDb = new ShopsDbContext(options);
        await using var ownerDb = new ShopsDbContext(options);
        var adminShop = await adminDb.Shops.SingleAsync(s => s.Id == shop.Id, ct);
        var ownerShop = await ownerDb.Shops.SingleAsync(s => s.Id == shop.Id, ct);
        adminShop.UpdateDetails("Admin edit", null, PriceRange.Cheap);
        adminShop.AdvanceProfileRevision();
        ownerShop.UpdateDetails("Owner edit", null, PriceRange.Cheap);
        ownerShop.AdvanceProfileRevision();

        await adminDb.SaveChangesAsync(ct);
        var error = await Assert.ThrowsAsync<ConflictException>(() => ownerDb.SaveChangesAsync(ct));
        Assert.Equal(409, error.StatusCode);
        Assert.Null(error.InnerException);
        Assert.False(TransientDbConflict.Matches(error));
        Assert.True(TransientDbConflict.Matches(new DbUpdateConcurrencyException()));

        await using var verify = new ShopsDbContext(options);
        var persisted = await verify.Shops.SingleAsync(s => s.Id == shop.Id, ct);
        Assert.Equal("Admin edit", persisted.Name);
        Assert.Equal(1, persisted.ProfileRevision);
    }
}
