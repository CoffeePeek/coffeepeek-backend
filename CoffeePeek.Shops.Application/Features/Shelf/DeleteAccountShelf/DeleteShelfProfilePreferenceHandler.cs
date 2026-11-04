using CoffeePeek.Contract.Events.Account;

namespace CoffeePeek.Shops.Application.Features.Shelf.DeleteAccountShelf;

public static class DeleteShelfProfilePreferenceHandler
{
    public static Task Handle(AccountDeletionRequestedV1 message, IShelfProfileStore profiles, CancellationToken ct) =>
        profiles.DeleteForOwnerAsync(message.UserId, ct);
}
