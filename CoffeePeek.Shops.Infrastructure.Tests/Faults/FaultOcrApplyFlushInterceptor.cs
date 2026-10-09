using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class FaultOcrApplyFlushInterceptor(string root, string checkpoint) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (checkpoint == "before-ocr-apply-commit" && eventData.Context!.ChangeTracker.Entries<MenuOcrJob>()
                .Any(e => e.Entity.Status == MenuOcrJobStatus.Applied))
        {
            Assert.True(await eventData.Context.Database.SqlQueryRaw<int>("""
                SELECT ((SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled')
                    + (SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes))::integer AS "Value"
                """).SingleAsync(cancellationToken) > 0);
            var ids = await eventData.Context.Database.SqlQueryRaw<Guid>("""
                SELECT id AS "Value" FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled'
                UNION SELECT id AS "Value" FROM wolverine.wolverine_outgoing_envelopes
                """).ToArrayAsync(cancellationToken);
            File.WriteAllLines(ShopsFaultControl.Marker(root, "private-envelopes"), ids.Select(id => id.ToString()));
            File.WriteAllText(ShopsFaultControl.Marker(root, checkpoint), "menu-and-durable-envelope-flushed-before-commit");
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        return result;
    }
}
