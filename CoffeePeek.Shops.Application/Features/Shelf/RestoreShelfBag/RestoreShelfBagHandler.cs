using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

namespace CoffeePeek.Shops.Application.Features.Shelf.RestoreShelfBag;

public static class RestoreShelfBagHandler
{
    public static async Task<Response<ShelfBagDto>> Handle(RestoreShelfBagCommand command, IShelfBagRepository bags,
        IShelfCatalogLookup catalog, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        await bags.LockOwnerAsync(command.UserId, ct);
        var bag = await ShelfBagAccess.GetOwnedAsync(bags, command.UserId, command.BagId, ct);
        var lineup = new ShelfLineup(command.UserId, await bags.GetOnShelfAsync(command.UserId, ct));
        lineup.Restore(bag, command.Position, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShelfBagDto>.Success(await ShelfBagMapping.ToDtoAsync(bag, catalog, ct));
    }
}
