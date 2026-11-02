using CoffeePeek.Contract.Events.Account;

namespace CoffeePeek.Shops.Application.Features.Discovery;

/// <summary>Confirmed account deletion disables its public collection links and removes their metadata. Idempotent.</summary>
public static class DeleteAccountFavoriteCollectionsHandler
{
    public static Task Handle(AccountDeletionRequestedV1 message, IFavoriteCollectionsStore collections, CancellationToken ct) =>
        collections.DeleteForOwnerAsync(message.UserId, ct);
}
