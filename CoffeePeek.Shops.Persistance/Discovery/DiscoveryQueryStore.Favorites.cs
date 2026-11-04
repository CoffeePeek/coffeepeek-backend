using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore
{
    private IQueryable<SavedCollectionItem> OwnedCollectionItems(Guid? owner, string kind) =>
        from c in db.SavedCollections.AsNoTracking()
        join i in db.Set<SavedCollectionItem>().AsNoTracking() on c.Id equals i.CollectionId
        where c.OwnerId == owner && c.RevokedAtUtc == null && i.Kind == kind
        select i;
}
