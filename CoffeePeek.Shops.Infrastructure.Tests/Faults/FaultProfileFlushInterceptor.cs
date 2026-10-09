using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class FaultProfileFlushInterceptor(string root, string checkpoint) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (checkpoint == "postgres-commit" && eventData.Context!.ChangeTracker.Entries<CoffeeShop>()
                .Any(e => e.Entity.ProfileRevision == 1))
        {
            var ids = await eventData.Context.Database.SqlQueryRaw<Guid>("""
                SELECT id AS "Value" FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled'
                UNION SELECT id AS "Value" FROM wolverine.wolverine_outgoing_envelopes
                """).ToArrayAsync(cancellationToken);
            File.WriteAllLines(ShopsFaultControl.Marker(root, "private-envelopes"), ids.Select(id => id.ToString()));
            File.WriteAllText(ShopsFaultControl.Marker(root, "profile-flushed"), "flushed-before-commit");
            await ShopsFaultControl.WaitMarkerAsync(root, "allow-commit", cancellationToken);
        }
        return result;
    }
}
