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
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
