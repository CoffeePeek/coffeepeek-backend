using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Shelf.DeleteShelfBag;

public static class DeleteShelfBagHandler
{
    public static async Task<Response> Handle(DeleteShelfBagCommand command, IShelfBagRepository bags, IMessageBus bus,
        IUnitOfWork unitOfWork, CancellationToken ct)
    {
        await bags.LockOwnerAsync(command.UserId, ct);
        var bag = await ShelfBagAccess.GetOwnedAsync(bags, command.UserId, command.BagId, ct);
        var lineup = new ShelfLineup(command.UserId, await bags.GetOnShelfAsync(command.UserId, ct));
        lineup.Remove(bag);
        bags.Remove(bag);
        if (bag.PhotoId is { } photoId)
            await bus.PublishAsync(new ShelfBagPhotoReleasedEvent(command.UserId, bag.Id, photoId, DateTime.UtcNow));
        await unitOfWork.SaveChangesAsync(ct);
        return Response.Success();
    }
}
