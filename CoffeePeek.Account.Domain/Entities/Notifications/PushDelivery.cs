namespace CoffeePeek.Account.Domain.Entities.Notifications;

public sealed class PushDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public PushNotification Notification { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public Guid DeviceRevision { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? Outcome { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
