namespace CoffeePeek.Account.Domain.Entities.Notifications;

public sealed class PushNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public PushNotificationCategory Category { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Status { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public NotificationType Type => (Category, EntityType, Status) switch
    {
        (PushNotificationCategory.NewShop, _, _) => NotificationType.NewShop,
        (PushNotificationCategory.Inactivity, _, _) => NotificationType.Inactivity,
        (PushNotificationCategory.Moderation, "Shop", "Approved") => NotificationType.ShopApproved,
        (PushNotificationCategory.Moderation, "Shop", "Rejected") => NotificationType.ShopRejected,
        (PushNotificationCategory.Moderation, "Roaster", "Approved") => NotificationType.RoasterApproved,
        (PushNotificationCategory.Moderation, "Roaster", "Rejected") => NotificationType.RoasterRejected,
        (PushNotificationCategory.Moderation, "CheckIn", _) => NotificationType.CheckInModeration,
        (PushNotificationCategory.Moderation, "ShopChange", _) => NotificationType.CorrectionModeration,
        _ => NotificationType.General
    };
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
