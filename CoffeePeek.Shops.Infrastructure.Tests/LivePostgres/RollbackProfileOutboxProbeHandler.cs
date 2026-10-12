using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

/// <summary>A fault after publication, inside Wolverine's production EF transaction middleware.</summary>
public static class RollbackProfileOutboxProbeHandler
{
    public static async Task Handle(
        RollbackProfileOutboxProbe command,
        ShopsDbContext db,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shop = await db.Shops.SingleAsync(s => s.Id == command.ShopId, ct);
        shop.UpdateDetails("Uncommitted profile", shop.Description, shop.PriceRange);
        shop.AdvanceProfileRevision();
        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));
        // An early EF flush must still be inside Wolverine's outer transaction.
        await db.SaveChangesAsync(ct);
        throw new InvalidOperationException("Injected failure after publication, before commit.");
    }
}
