using CoffeePeek.Contract.Dtos.Notifications;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Application.Features.Notifications;

/// <summary>Resolves an owned notification using current public identity, without changing read state.</summary>
public sealed class NotificationDestinationResolver(IPushNotificationStore store, INotificationShopAddressLookup shops)
{
    public async Task<NotificationDestinationDto?> ResolveAsync(Guid userId, Guid notificationId, CancellationToken ct)
    {
        var item = await store.GetNotificationAsync(userId, notificationId, ct);
        if (item.EntityId is not { } id || id == Guid.Empty) return null;
        if (item.Type == "NewShop" && item.EntityType == "Shop")
        {
            var address = await shops.FindAsync(id, ct)
                ?? throw new NotFoundException("The notification target is no longer available.");
            return new("Shop", address.Slug);
        }
        var kind = (item.Type, item.EntityType) switch
        {
            ("CorrectionModeration", "ShopChange") => "ShopChange",
            ("ShopApproved" or "ShopRejected", "Shop") => "ShopSubmission",
            ("RoasterApproved" or "RoasterRejected", "Roaster") => "RoasterSubmission",
            ("CheckInModeration", "CheckIn") => "CheckIns",
            _ => null
        };
        return kind is null ? null : new(kind, id.ToString("D"));
    }
}
