using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

namespace CoffeePeek.Shops.Application.Features.Shelf;

internal static class ShelfBagAccess
{
    /// <summary>Another owner's bag is indistinguishable from a missing one.</summary>
    public static async Task<ShelfBag> GetOwnedAsync(IShelfBagRepository bags, Guid userId, Guid bagId, CancellationToken ct)
    {
        var bag = await bags.GetAsync(bagId, ct);
        return bag != null && bag.IsOwnedBy(userId) ? bag : throw new NotFoundException("Shelf bag not found.");
    }
}
