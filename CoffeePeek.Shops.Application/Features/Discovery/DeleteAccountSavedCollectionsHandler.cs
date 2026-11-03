using CoffeePeek.Contract.Events.Account;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class DeleteAccountSavedCollectionsHandler
{
    public static Task Handle(AccountDeletionRequestedV1 message, ISavedCollectionsStore collections, CancellationToken ct) =>
        collections.DeleteForOwnerAsync(message.UserId, ct);
}
