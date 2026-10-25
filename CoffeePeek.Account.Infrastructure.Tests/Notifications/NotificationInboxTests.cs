using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Account.Infrastructure.Tests.Persistence;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Notifications;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.Notifications;

public sealed class NotificationInboxTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InboxIsPrivatePagedAndAvailableWithoutDevicesOrPushConsent()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var alice = ProfileSocialStoreTests.NewUser("Alice");
        var bob = ProfileSocialStoreTests.NewUser("Bob");
        db.Users.AddRange(alice, bob); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        await store.SetPushEnabledAsync(alice.Id, false, Ct);
        var first = Notification(alice.Id, DateTime.UtcNow.AddMinutes(-2));
        var second = Notification(alice.Id, DateTime.UtcNow.AddMinutes(-1));
        var foreign = Notification(bob.Id, DateTime.UtcNow);
        await store.EnqueueAsync(first, Ct); await store.EnqueueAsync(second, Ct); await store.EnqueueAsync(foreign, Ct);
        var page = await store.GetInboxAsync(alice.Id, null, 1, Ct);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        Assert.Equal(2, page.UnreadCount);
        Assert.Equal(second.Id, page.NextCursor);
        var next = await store.GetInboxAsync(alice.Id, page.NextCursor, 1, Ct);
        Assert.Equal(first.Id, Assert.Single(next.Items).Id);
        Assert.Null(next.NextCursor);
        Assert.Empty(db.PushDeliveries);
        Assert.Equal(first.Id, (await store.GetNotificationAsync(alice.Id, first.Id, Ct)).Id);
        Assert.Null(first.ReadAtUtc);
        await Assert.ThrowsAsync<NotFoundException>(() => store.GetNotificationAsync(alice.Id, foreign.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => store.MarkReadAsync(alice.Id, foreign.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => store.GetInboxAsync(alice.Id, foreign.Id, 30, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => store.MarkAllReadAsync(alice.Id, foreign.Id, Ct));
        await Assert.ThrowsAsync<ValidationException>(() => store.GetInboxAsync(alice.Id, null, 101, Ct));
        await store.MarkReadAsync(alice.Id, second.Id, Ct);
        await store.MarkReadAsync(alice.Id, second.Id, Ct);
        Assert.Equal(1, (await store.GetInboxAsync(alice.Id, null, 30, Ct)).UnreadCount);
        Assert.Null(foreign.ReadAtUtc);
    }

    [Fact]
    public async Task ReadAllStopsAtSnapshotAndDisablingPushCancelsPendingDeliveriesWithoutDeletingHistory()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var user = ProfileSocialStoreTests.NewUser("Owner");
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        await store.RegisterDeviceAsync(user.Id, Guid.NewGuid(), "token", Ct);
        var old = Notification(user.Id, DateTime.UtcNow.AddMinutes(-1));
        await store.EnqueueAsync(old, Ct);
        var newer = Notification(user.Id, DateTime.UtcNow);
        await store.EnqueueAsync(newer, Ct);
        await store.MarkAllReadAsync(user.Id, old.Id, Ct);
        Assert.NotNull(old.ReadAtUtc);
        Assert.Null(newer.ReadAtUtc);
        await store.SetPushEnabledAsync(user.Id, false, Ct);
        Assert.False((await store.GetPushSettingsAsync(user.Id, Ct)).Enabled);
        await store.SetPreferencesAsync(user.Id, new(), Ct); // Older clients cannot reset the master switch.
        Assert.False((await store.GetPushSettingsAsync(user.Id, Ct)).Enabled);
        await store.SetPushEnabledAsync(user.Id, true, Ct);
        Assert.Null(await store.ClaimAsync(Ct));
        Assert.Equal(2, (await store.GetInboxAsync(user.Id, null, 30, Ct)).Items.Count);
    }

    [Fact]
    public async Task DelayedPublicationStaysUnreadWhenReadingAnEarlierInboxSnapshot()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var user = ProfileSocialStoreTests.NewUser("LatePublication");
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        var anchor = Notification(user.Id, DateTime.UtcNow);
        await store.EnqueueAsync(anchor, Ct);
        await store.QueueNewShopBatchAsync(Guid.NewGuid(), "Новая кофейня", DateTime.UtcNow.AddDays(-1), null, Ct);
        await store.MarkAllReadAsync(user.Id, anchor.Id, Ct);
        var publication = await db.PushNotifications.SingleAsync(x => x.Category == PushNotificationCategory.NewShop, Ct);
        Assert.Null(publication.ReadAtUtc);
        Assert.True(publication.CreatedAtUtc > anchor.CreatedAtUtc);
        Assert.Equal(1, (await store.GetInboxAsync(user.Id, null, 30, Ct)).UnreadCount);
    }

    [Fact]
    public async Task NewShopBroadcastUsesBatchesExcludesInactiveAccountsAndIsIdempotent()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var users = Enumerable.Range(0, 201).Select(i => ProfileSocialStoreTests.NewUser("User" + i)).ToArray();
        var blocked = ProfileSocialStoreTests.NewUser("Blocked");
        blocked.SetBlocked(true);
        db.Users.AddRange(users); db.Users.Add(blocked); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        await store.RegisterDeviceAsync(users[0].Id, Guid.NewGuid(), "token", Ct);
        await store.SetPushEnabledAsync(users[0].Id, false, Ct);
        var shop = Guid.NewGuid(); var published = DateTime.UtcNow;
        var after = await store.QueueNewShopBatchAsync(shop, "Новая кофейня", published, null, Ct);
        Assert.NotNull(after);
        Assert.Null(await store.QueueNewShopBatchAsync(shop, "Новая кофейня", published, after, Ct));
        await store.QueueNewShopBatchAsync(shop, "Новая кофейня", published, null, Ct);
        Assert.Equal(201, await db.PushNotifications.CountAsync(Ct));
        Assert.Empty(db.PushDeliveries);
        Assert.All(await db.PushNotifications.ToArrayAsync(Ct), n =>
        {
            Assert.Equal(NotificationType.NewShop, n.Type);
            Assert.Equal(shop, n.EntityId);
            Assert.NotEqual(blocked.Id, n.UserId);
        });
    }

    [Theory]
    [InlineData("Shop", "Approved", NotificationType.ShopApproved)]
    [InlineData("Shop", "Rejected", NotificationType.ShopRejected)]
    [InlineData("Roaster", "Approved", NotificationType.RoasterApproved)]
    [InlineData("Roaster", "Rejected", NotificationType.RoasterRejected)]
    [InlineData("CheckIn", "Rejected", NotificationType.CheckInModeration)]
    [InlineData("ShopChange", "Approved", NotificationType.CorrectionModeration)]
    [InlineData("Other", "Other", NotificationType.General)]
    public void TypesRemainSpecificAndUnknownEventsRemainReadable(string entity, string status, NotificationType expected) =>
        Assert.Equal(expected, new PushNotification { Category = PushNotificationCategory.Moderation, EntityType = entity, Status = status }.Type);

    [Fact]
    public void PostgreSqlTranslatesStableNotificationAndRecipientCursors()
    {
        using var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only").Options);
        var id = Guid.NewGuid(); var at = DateTime.UtcNow;
        var inboxSql = db.PushNotifications.Where(x => x.CreatedAtUtc < at ||
            (x.CreatedAtUtc == at && x.Id.CompareTo(id) < 0)).OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).ToQueryString();
        var recipientsSql = db.Users.Where(x => x.Id.CompareTo(id) > 0).OrderBy(x => x.Id).Take(200).ToQueryString();
        Assert.Contains("ORDER BY", inboxSql);
        Assert.Contains("LIMIT", recipientsSql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static PushNotification Notification(Guid userId, DateTime at) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Category = PushNotificationCategory.Custom,
        Title = "Новости", Body = "Сообщение", CreatedAtUtc = at, ExpiresAtUtc = at.AddDays(1)
    };
}
