using System.Data.Common;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class FaultOcrStoredInterceptor(string root, string checkpoint) : DbTransactionInterceptor
{
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (checkpoint == "after-ocr-store" && eventData.Context!.ChangeTracker.Entries<MenuOcrJob>()
                .Any(e => e.Entity.Status == MenuOcrJobStatus.ResultStored))
        {
            File.WriteAllText(ShopsFaultControl.Marker(root, checkpoint), "result-committed-before-apply");
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
