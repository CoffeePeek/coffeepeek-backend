using CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface ICoffeeCatalogTagStorage
{
    Task<bool> IsSlugReservedAsync(string slug, CancellationToken ct);
    Task<RoasterTag?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> RoasterExistsAsync(Guid id, CancellationToken ct);
    Task<int> CountActiveAsync(Guid[] ids, CancellationToken ct);
    Task<RoasterTagAssignment[]> AssignmentsAsync(Guid roasterId, CancellationToken ct);
    Task SaveTagAsync(RoasterTag tag, bool add, CancellationToken ct);
    Task SaveAssignmentsAsync(RoasterTagAssignment[] added, RoasterTagAssignment[] removed, CancellationToken ct);
}
