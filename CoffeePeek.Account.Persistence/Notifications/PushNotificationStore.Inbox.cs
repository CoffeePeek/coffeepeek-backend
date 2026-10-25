using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Contract.Dtos.Notifications;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Notifications;

public sealed partial class PushNotificationStore
{
    public async Task<NotificationPageDto> GetInboxAsync(Guid userId, Guid? before, int pageSize, CancellationToken ct)
    {
        if (pageSize is < 1 or > 100) throw new ValidationException("Page size must be between 1 and 100.");
        await EnsureUserAsync(userId, ct);
        var query = db.PushNotifications.AsNoTracking().Where(x => x.UserId == userId);
        if (before is { } cursor)
        {
            var anchor = await OwnedNotificationAsync(userId, cursor, ct);
            query = query.Where(x => x.CreatedAtUtc < anchor.CreatedAtUtc
                || (x.CreatedAtUtc == anchor.CreatedAtUtc && x.Id.CompareTo(anchor.Id) < 0));
        }
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(pageSize + 1).ToArrayAsync(ct);
        var page = rows.Take(pageSize).ToArray();
        var unread = await db.PushNotifications.CountAsync(x => x.UserId == userId && x.ReadAtUtc == null, ct);
        return new(page.Select(ToDto).ToArray(),
            rows.Length > pageSize ? page[^1].Id : null, unread);
    }

    public async Task<NotificationDto> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        return ToDto(await OwnedNotificationAsync(userId, notificationId, ct));
    }

    private static NotificationDto ToDto(PushNotification row) => new(row.Id, row.Type.ToString(), row.Title, row.Body,
        row.CreatedAtUtc, row.ReadAtUtc, row.EntityType, row.EntityId, row.Status);

    public Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct) => LockedAsync(userId, async () =>
    {
        await EnsureUserAsync(userId, ct);
        var row = await OwnedNotificationAsync(userId, notificationId, ct);
        row.ReadAtUtc ??= Now;
        await db.SaveChangesAsync(ct);
    }, ct);

    public Task MarkAllReadAsync(Guid userId, Guid throughId, CancellationToken ct) => LockedAsync(userId, async () =>
    {
        await EnsureUserAsync(userId, ct);
        var anchor = await OwnedNotificationAsync(userId, throughId, ct);
        var query = db.PushNotifications.Where(x => x.UserId == userId && x.ReadAtUtc == null
            && (x.CreatedAtUtc < anchor.CreatedAtUtc
                || (x.CreatedAtUtc == anchor.CreatedAtUtc && x.Id.CompareTo(anchor.Id) <= 0)));
        if (db.Database.IsRelational()) await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, Now), ct);
        else
        {
            foreach (var row in await query.ToArrayAsync(ct)) row.ReadAtUtc = Now;
            await db.SaveChangesAsync(ct);
        }
    }, ct);

    public async Task<PushSettingsDto> GetPushSettingsAsync(Guid userId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        var row = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return new(row?.PushEnabled ?? true);
    }

    public Task SetPushEnabledAsync(Guid userId, bool enabled, CancellationToken ct) => LockedAsync(userId, async () =>
    {
        await EnsureUserAsync(userId, ct);
        (await PreferencesAsync(userId, ct)).PushEnabled = enabled;
        if (!enabled)
        {
            var pending = db.PushDeliveries.Where(x => x.Notification.UserId == userId && x.FinishedAtUtc == null);
            if (db.Database.IsRelational())
                await pending.ExecuteUpdateAsync(s => s.SetProperty(x => x.FinishedAtUtc, Now)
                    .SetProperty(x => x.Outcome, "Skipped").SetProperty(x => x.Revision, Guid.NewGuid()), ct);
            else foreach (var delivery in await pending.ToArrayAsync(ct))
            {
                delivery.FinishedAtUtc = Now;
                delivery.Outcome = "Skipped";
                delivery.Revision = Guid.NewGuid();
            }
        }
        await db.SaveChangesAsync(ct);
    }, ct);

    public async Task<Guid?> QueueNewShopBatchAsync(Guid shopId, string name, DateTime publishedAtUtc, Guid? afterUserId, CancellationToken ct)
    {
        if (shopId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 300 || publishedAtUtc.Kind != DateTimeKind.Utc)
            throw new ValidationException("Invalid published shop notification.");
        var users = db.Users.AsNoTracking().Where(x => !x.IsBlocked && !x.IsSoftDelete);
        if (afterUserId is { } after) users = users.Where(x => x.Id.CompareTo(after) > 0);
        var recipients = await users.OrderBy(x => x.Id).Select(x => x.Id).Take(200).ToArrayAsync(ct);
        var message = NotificationTemplateRenderer.Render(await GetTemplateAsync(PushNotificationCategory.NewShop, ct), shopName: name);
        foreach (var userId in recipients)
        {
            // A replay after a crash must reuse the notification ID for this shop and recipient.
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"new-shop:{shopId:N}:{userId:N}")).AsSpan(0, 16));
            await EnqueueAsync(new PushNotification
            {
                Id = id, UserId = userId, Category = PushNotificationCategory.NewShop,
                Title = message.Title, Body = message.Body,
                EntityType = "Shop", EntityId = shopId, Status = "Published",
                CreatedAtUtc = publishedAtUtc, ExpiresAtUtc = publishedAtUtc.AddDays(7)
            }, ct);
        }
        return recipients.Length == 200 ? recipients[^1] : null;
    }

    private async Task<PushNotification> OwnedNotificationAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.PushNotifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
        ?? throw new NotFoundException("Notification not found.");
}
