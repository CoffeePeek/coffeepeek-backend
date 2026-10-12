using System.Data.Common;
using CoffeePeek.MediaService.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class FaultMediaCommitInterceptor(string root, string checkpoint) : DbTransactionInterceptor
{
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (checkpoint == "after-media-commit" && eventData.Context is MediaDbContext db
            && db.ChangeTracker.Entries<PhotoMetadata>().Any(e => e.Entity.Status == PhotoStatus.Confirmed))
        {
            File.WriteAllText(ShopsFaultControl.Marker(root, "media-committed"), "committed-before-ack");
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
