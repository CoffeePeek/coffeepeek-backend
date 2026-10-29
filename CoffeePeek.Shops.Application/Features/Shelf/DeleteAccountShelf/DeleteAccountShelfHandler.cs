using CoffeePeek.Contract.Events;
using CoffeePeek.Contract.Events.Account;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Shelf.DeleteAccountShelf;

/// <summary>A confirmed account deletion removes the whole private shelf and releases its cards. Idempotent.</summary>
public static class DeleteAccountShelfHandler
{
    public static async Task Handle(AccountDeletionRequestedV1 message, IShelfBagRepository bags, IMessageBus bus,
        IUnitOfWork unitOfWork, CancellationToken ct)
    {
        await bags.LockOwnerAsync(message.UserId, ct);
        var now = DateTime.UtcNow;
        foreach (var bag in await bags.GetAllAsync(message.UserId, ct))
        {
            bags.Remove(bag);
            if (bag.PhotoId is { } photoId)
                await bus.PublishAsync(new ShelfBagPhotoReleasedEvent(message.UserId, bag.Id, photoId, now));
        }
        await unitOfWork.SaveChangesAsync(ct);
    }
}
