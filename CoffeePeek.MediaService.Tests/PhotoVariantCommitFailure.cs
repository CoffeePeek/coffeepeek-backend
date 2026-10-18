using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoVariantCommitFailure : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Injected failure after Ready and envelope, before commit.");
}
