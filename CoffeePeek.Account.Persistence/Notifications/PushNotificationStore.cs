using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Profiles;
using CoffeePeek.Contract.Dtos.Notifications;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Notifications;

public sealed class PushNotificationStore(AccountDbContext db, TimeProvider time) : IPushNotificationStore
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task RegisterDeviceAsync(Guid userId, Guid installationId, string token, CancellationToken ct)
    {
        if (installationId == Guid.Empty || string.IsNullOrWhiteSpace(token) || token.Length > 4096
            || token.Any(char.IsWhiteSpace)) throw new ValidationException("A valid installation ID and FCM token are required.");
        // Serialize token transfers between installations as well as concurrent token refreshes.
        await ProfileRelationshipTransaction.RunAsync(db, Guid.Empty, Guid.Empty, () =>
            LockedAsync(userId, async () =>
            {
                await EnsureUserAsync(userId, ct);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
                var device = await db.PushDevices.FindAsync([installationId], ct);
                var duplicate = await db.PushDevices.SingleOrDefaultAsync(x => x.TokenHash == hash && x.Id != installationId, ct);
                if (duplicate is not null)
                {
                    db.PushDevices.Remove(duplicate);
                    await db.SaveChangesAsync(ct);
                }
                if ((device is null || device.UserId != userId)
                    && await db.PushDevices.CountAsync(x => x.UserId == userId, ct) >= 10)
                    throw new ValidationException("At most ten push devices are allowed per account.");
                if (device is null)
                {
                    device = new PushDevice { Id = installationId };
                    db.PushDevices.Add(device);
                }
                if (device.UserId != userId) device.Revision = Guid.NewGuid();
                device.UserId = userId;
                device.Token = token;
                device.TokenHash = hash;
                device.UpdatedAtUtc = Now;
                (await PreferencesAsync(userId, ct)).RecordActivity(Now, false);
                await db.SaveChangesAsync(ct);
            }, ct), ct);
    }

    public Task RemoveDeviceAsync(Guid userId, Guid installationId, CancellationToken ct) => LockedAsync(userId, async () =>
    {
        var device = await db.PushDevices.SingleOrDefaultAsync(x => x.Id == installationId && x.UserId == userId, ct);
        if (device is not null) db.PushDevices.Remove(device);
        await db.SaveChangesAsync(ct);
    }, ct);

    public async Task<NotificationPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        var row = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return row is null ? new() : new(row.Moderation, row.Inactivity, row.Custom);
    }

    public Task SetPreferencesAsync(Guid userId, NotificationPreferencesDto preferences, CancellationToken ct) => LockedAsync(userId, async () =>
    {
        await EnsureUserAsync(userId, ct);
        var row = await PreferencesAsync(userId, ct);
        row.Moderation = preferences.Moderation;
        row.Inactivity = preferences.Inactivity;
        row.Custom = preferences.Custom;
        row.RecordActivity(Now, false);
        await db.SaveChangesAsync(ct);
    }, ct);

    public Task RecordActivityAsync(Guid userId, DateTime atUtc, bool checkIn, CancellationToken ct) => LockedAsync(userId, async () =>
    {
        if (!await ActiveUserAsync(userId, ct)) return;
        if (atUtc > Now) atUtc = Now;
        (await PreferencesAsync(userId, ct)).RecordActivity(atUtc, checkIn);
        await db.SaveChangesAsync(ct);
    }, ct);

    public Task EnqueueAsync(PushNotification notification, CancellationToken ct) => LockedAsync(notification.UserId, async () =>
    {
        await EnqueueCoreAsync(notification, ct);
        await db.SaveChangesAsync(ct);
    }, ct);

    private async Task EnqueueCoreAsync(PushNotification n, CancellationToken ct)
    {
        if (n.Id == Guid.Empty || n.UserId == Guid.Empty || !Enum.IsDefined(n.Category)
            || string.IsNullOrWhiteSpace(n.Title) || n.Title.Length > 120
            || string.IsNullOrWhiteSpace(n.Body) || n.Body.Length > 500
            || n.EntityType?.Length > 40 || n.Status?.Length > 40 || n.ExpiresAtUtc <= n.CreatedAtUtc)
            throw new ValidationException("Invalid push notification.");
        var existing = await db.PushNotifications.FindAsync([n.Id], ct);
        if (existing is not null)
        {
            if (existing.UserId != n.UserId || existing.CreatedByUserId != n.CreatedByUserId
                || (n.Category == PushNotificationCategory.Custom && (existing.Title != n.Title || existing.Body != n.Body))
                || existing.EntityId != n.EntityId || existing.EntityType != n.EntityType
                || existing.Status != n.Status || existing.Category != n.Category)
                throw new ConflictException("Notification ID was already used with different content.");
            // A redelivered moderation event keeps its original text even if an admin has edited the template.
            return;
        }
        if (!await ActiveUserAsync(n.UserId, ct))
        {
            if (n.CreatedByUserId is not null) throw new NotFoundException("User not found.");
            return;
        }
        var preferences = await db.NotificationPreferences.FindAsync([n.UserId], ct);
        db.PushNotifications.Add(n);
        if (!Allowed(preferences, n.Category)) return;
        var devices = await db.PushDevices.Where(x => x.UserId == n.UserId).ToArrayAsync(ct);
        db.PushDeliveries.AddRange(devices.Select(x => new PushDelivery
        {
            Notification = n, NotificationId = n.Id, DeviceId = x.Id, DeviceRevision = x.Revision,
            NextAttemptAtUtc = Now
        }));
    }

    public async Task QueueRemindersAsync(int inactiveDays, int repeatDays, CancellationToken ct)
    {
        var template = NotificationTemplateRenderer.Render(await GetTemplateAsync(PushNotificationCategory.Inactivity, ct));
        var now = Now;
        var cutoff = now.AddDays(-inactiveDays);
        var repeat = now.AddDays(-repeatDays);
        var users = await db.NotificationPreferences.AsNoTracking()
            .Where(x => x.Inactivity && x.LastSeenAtUtc <= cutoff
                && (x.LastCheckInAtUtc == null || x.LastCheckInAtUtc <= cutoff)
                && (x.LastReminderAtUtc == null || x.LastReminderAtUtc <= repeat)
                && db.PushDevices.Any(d => d.UserId == x.UserId)
                && db.Users.Any(u => u.Id == x.UserId && !u.IsSoftDelete && !u.IsBlocked))
            .OrderBy(x => x.LastReminderAtUtc).ThenBy(x => x.UserId).Select(x => x.UserId).Take(100).ToArrayAsync(ct);
        foreach (var userId in users)
        {
            await LockedAsync(userId, async () =>
            {
                var row = await db.NotificationPreferences.FindAsync([userId], ct);
                if (row is null || !row.IsInactive(Now, inactiveDays, repeatDays)) return;
                row.LastReminderAtUtc = Now;
                await EnqueueCoreAsync(new PushNotification
                {
                    Id = Guid.NewGuid(), UserId = userId, Category = PushNotificationCategory.Inactivity,
                    Title = template.Title, Body = template.Body,
                    CreatedAtUtc = Now, ExpiresAtUtc = Now.AddDays(1)
                }, ct);
                await db.SaveChangesAsync(ct);
            }, ct);
        }
    }

    public async Task<PushDispatch?> ClaimAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = Now;
        var delivery = await db.PushDeliveries.Include(x => x.Notification)
            .Where(x => x.FinishedAtUtc == null && x.NextAttemptAtUtc <= now
                && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
            .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (delivery is null) return null;
        var n = delivery.Notification;
        var device = await db.PushDevices.FindAsync([delivery.DeviceId], ct);
        var preferences = await db.NotificationPreferences.FindAsync([n.UserId], ct);
        if (device is null || device.UserId != n.UserId || device.Revision != delivery.DeviceRevision
            || !await ActiveUserAsync(n.UserId, ct) || !Allowed(preferences, n.Category)
            || n.ExpiresAtUtc <= now || delivery.Attempts >= 8
            || (n.Category == PushNotificationCategory.Inactivity && preferences is not null
                && (preferences.LastSeenAtUtc > n.CreatedAtUtc || preferences.LastCheckInAtUtc > n.CreatedAtUtc)))
        {
            delivery.FinishedAtUtc = now;
            delivery.Outcome = "Skipped";
        }
        delivery.Revision = Guid.NewGuid();
        delivery.LeaseUntilUtc = now.AddMinutes(2);
        delivery.Attempts++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return null; }
        return delivery.FinishedAtUtc is null ? new(delivery.Id, delivery.Revision, device!.Token, n) : null;
    }

    public async Task CompleteAsync(PushDispatch dispatch, PushSendResult result, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var row = await db.PushDeliveries.SingleOrDefaultAsync(x => x.Id == dispatch.DeliveryId && x.Revision == dispatch.LeaseId, ct);
        if (row is null) return;
        row.Outcome = result.Outcome;
        row.LeaseUntilUtc = null;
        row.Revision = Guid.NewGuid();
        if (result.RetryAfter is { } delay && row.Attempts < 8)
            row.NextAttemptAtUtc = Now.Add(delay > TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, row.Attempts)))
                ? delay : TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, row.Attempts))));
        else
        {
            row.FinishedAtUtc = Now;
            if (result.RetryAfter is not null) row.Outcome = "Exhausted";
        }
        if (result.Outcome == "Unregistered")
        {
            var device = await db.PushDevices.SingleOrDefaultAsync(x => x.Id == row.DeviceId
                && x.Revision == row.DeviceRevision && x.Token == dispatch.Token, ct);
            if (device is not null) db.PushDevices.Remove(device);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task PurgeAsync(CancellationToken ct)
    {
        var cutoff = Now.AddDays(-30);
        await db.PushNotifications.Where(x => x.ExpiresAtUtc < cutoff).ExecuteDeleteAsync(ct);
        await db.PushDevices.Where(x => x.UpdatedAtUtc < cutoff
            || db.Users.Any(u => u.Id == x.UserId && (u.IsSoftDelete || u.IsBlocked))).ExecuteDeleteAsync(ct);
    }

    private static bool Allowed(NotificationPreferences? p, PushNotificationCategory category) => category switch
    {
        PushNotificationCategory.Moderation => p?.Moderation ?? true,
        PushNotificationCategory.Inactivity => p?.Inactivity ?? false,
        PushNotificationCategory.Custom => p?.Custom ?? true,
        _ => false
    };

    public async Task<PushNotificationTemplate> GetTemplateAsync(PushNotificationCategory category, CancellationToken ct) =>
        await db.PushNotificationTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Category == category, ct)
        ?? NotificationTemplateRenderer.Default(category);

    public async Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesAsync(CancellationToken ct)
    {
        var result = new List<NotificationTemplateDto>();
        foreach (var category in new[] { PushNotificationCategory.Moderation, PushNotificationCategory.Inactivity })
        {
            var row = await GetTemplateAsync(category, ct);
            result.Add(new(category.ToString(), row.Title, row.Body, row.UpdatedAtUtc == default ? null : row.UpdatedAtUtc));
        }
        return result;
    }

    public Task SetTemplateAsync(PushNotificationTemplate template, CancellationToken ct)
    {
        NotificationTemplateRenderer.Validate(template);
        return LockedAsync(Guid.Empty, async () =>
        {
            var row = await db.PushNotificationTemplates.FindAsync([template.Category], ct);
            if (row is null)
            {
                row = new PushNotificationTemplate { Category = template.Category };
                db.PushNotificationTemplates.Add(row);
            }
            row.Title = template.Title.Trim();
            row.Body = template.Body.Trim();
            row.UpdatedAtUtc = Now;
            row.UpdatedByUserId = template.UpdatedByUserId;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private async Task<NotificationPreferences> PreferencesAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.NotificationPreferences.FindAsync([userId], ct);
        if (row is not null) return row;
        row = new NotificationPreferences { UserId = userId, LastSeenAtUtc = Now };
        db.NotificationPreferences.Add(row);
        return row;
    }

    private Task<bool> ActiveUserAsync(Guid id, CancellationToken ct) => db.Users.AnyAsync(x => x.Id == id && !x.IsBlocked && !x.IsSoftDelete, ct);

    private async Task EnsureUserAsync(Guid id, CancellationToken ct)
    {
        if (!await ActiveUserAsync(id, ct)) throw new NotFoundException("User not found.");
    }

    private Task LockedAsync(Guid userId, Func<Task> action, CancellationToken ct) =>
        ProfileRelationshipTransaction.RunAsync(db, userId, userId, action, ct);
}
