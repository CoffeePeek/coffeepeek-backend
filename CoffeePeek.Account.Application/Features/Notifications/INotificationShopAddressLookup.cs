using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Account.Application.Features.Notifications;

public interface INotificationShopAddressLookup
{
    Task<PublicAddressMetadata?> FindAsync(Guid shopId, CancellationToken ct);
}
