using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

namespace CoffeePeek.Shops.Application.Features.Shelf.ReorderShelf;

public static class ReorderShelfHandler
{
    public static async Task<Response<ShelfBagDto[]>> Handle(ReorderShelfCommand command, IShelfBagRepository bags,
        IShelfCatalogLookup catalog, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        await bags.LockOwnerAsync(command.UserId, ct);
        var lineup = new ShelfLineup(command.UserId, await bags.GetOnShelfAsync(command.UserId, ct));
        lineup.Reorder(command.BagIds);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShelfBagDto[]>.Success(await ShelfBagMapping.ToDtosAsync(lineup.Bags.ToArray(), catalog, ct));
    }
}
