using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Domain.Entities.Notifications;

public sealed class NotificationPreferences
{
    public Guid UserId { get; set; }
    public bool Moderation { get; set; } = true;
    public bool Inactivity { get; set; }
    public bool Custom { get; set; } = true;
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? LastCheckInAtUtc { get; set; }
    public DateTime? LastReminderAtUtc { get; set; }

    public void RecordActivity(DateTime atUtc, bool checkIn)
    {
        if (atUtc.Kind != DateTimeKind.Utc) throw new ValidationException("UTC activity time is required.");
        if (checkIn)
        {
            if (LastCheckInAtUtc is null || atUtc > LastCheckInAtUtc) LastCheckInAtUtc = atUtc;
        }
        else if (atUtc > LastSeenAtUtc) LastSeenAtUtc = atUtc;
    }

    public bool IsInactive(DateTime nowUtc, int days, int repeatDays) => Inactivity
        && LastSeenAtUtc <= nowUtc.AddDays(-days)
        && (LastCheckInAtUtc == null || LastCheckInAtUtc <= nowUtc.AddDays(-days))
        && (LastReminderAtUtc == null || LastReminderAtUtc <= nowUtc.AddDays(-repeatDays));
}
