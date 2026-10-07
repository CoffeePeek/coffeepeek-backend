using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoffeePeek.Shops.Persistance.Configuration;

/// <summary>Canonical factories own visit creation time, including authenticated historical imports.</summary>
public sealed class CheckInAuditInterceptor : AuditInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken ct = default)
    {
        var timestamps = eventData.Context!.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added && e.Entity is CheckIn or ShopPhoto)
            .Select(e => (Entry: e, Created: (DateTime)e.Property("CreatedAtUtc").CurrentValue!))
            .Where(e => e.Created != default).ToArray();
        var outcome = await base.SavingChangesAsync(eventData, result, ct);
        foreach (var timestamp in timestamps) timestamp.Entry.Property("CreatedAtUtc").CurrentValue = timestamp.Created;
        return outcome;
    }
}
