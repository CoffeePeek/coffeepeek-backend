using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;

namespace CoffeePeek.Account.Infrastructure.Notifications;

public sealed class NotificationShopAddressLookup(PublicAddressHttpClient client) : INotificationShopAddressLookup
{
    public async Task<PublicAddressMetadata?> FindAsync(Guid shopId, CancellationToken ct) =>
        (await client.GetByIdsAsync("notification-target-shops", "shops", [shopId], ct))
            .SingleOrDefault(address => address.EntityId == shopId);
}
