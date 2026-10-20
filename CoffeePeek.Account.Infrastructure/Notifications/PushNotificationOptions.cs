using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Account.Infrastructure.Notifications;

public sealed class PushNotificationOptions
{
    public bool Enabled { get; set; }
    public string ProjectId { get; set; } = "";
    [Range(1, 365)] public int InactiveDays { get; set; } = 7;
    [Range(1, 365)] public int ReminderRepeatDays { get; set; } = 7;
}
