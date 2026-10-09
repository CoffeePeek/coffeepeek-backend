using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed class ImportAfterFlushProbe(Func<ShopsDbContext, Task> observe) : SaveChangesInterceptor
{
    private bool observed;

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (!observed && eventData.Context!.ChangeTracker.Entries<CoffeeImportRun>().Any(e => e.Entity.Status == "Applied"))
        {
            observed = true;
            await observe((ShopsDbContext)eventData.Context);
        }
        return result;
    }
}
