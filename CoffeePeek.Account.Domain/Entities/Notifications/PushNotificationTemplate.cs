namespace CoffeePeek.Account.Domain.Entities.Notifications;

public sealed class PushNotificationTemplate
{
    public PushNotificationCategory Category { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public Guid UpdatedByUserId { get; set; }
}
