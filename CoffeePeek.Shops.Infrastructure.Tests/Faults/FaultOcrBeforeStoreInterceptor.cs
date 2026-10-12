using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class FaultOcrBeforeStoreInterceptor(string root, string checkpoint) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (checkpoint == "before-ocr-store" && eventData.Context!.ChangeTracker.Entries<MenuOcrJob>()
                .Any(e => e.Entity.Status == MenuOcrJobStatus.ResultStored))
        {
            File.WriteAllText(ShopsFaultControl.Marker(root, checkpoint), "provider-returned-result-not-persisted");
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        return result;
    }
}
