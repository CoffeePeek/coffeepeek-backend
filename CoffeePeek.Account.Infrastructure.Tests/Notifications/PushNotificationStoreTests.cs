using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Account.Infrastructure.Tests.Persistence;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Notifications;
using CoffeePeek.Contract.Dtos.Notifications;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.Notifications;

public sealed class PushNotificationStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queue_DeduplicatesAndSendsToEachDeviceOnce()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var user = ProfileSocialStoreTests.NewUser("Owner");
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        await store.RegisterDeviceAsync(user.Id, Guid.NewGuid(), "first-token", Ct);
        await store.RegisterDeviceAsync(user.Id, Guid.NewGuid(), "second-token", Ct);
        var notification = Notification(user.Id);
        notification.Category = PushNotificationCategory.Custom;
        await store.EnqueueAsync(notification, Ct);
        var retry = Notification(user.Id, notification.Id); retry.Category = PushNotificationCategory.Custom;
        await store.EnqueueAsync(retry, Ct);
        Assert.Equal(2, await db.PushDeliveries.CountAsync(Ct));
        for (var i = 0; i < 2; i++)
        {
            var dispatch = Assert.IsType<PushDispatch>(await store.ClaimAsync(Ct));
            await store.CompleteAsync(dispatch, PushSendResult.Accepted, Ct);
        }
        Assert.Null(await store.ClaimAsync(Ct));
        Assert.All(await db.PushDeliveries.ToArrayAsync(Ct), x => Assert.Equal("Accepted", x.Outcome));
        var changed = Notification(user.Id, notification.Id); changed.Title = "Different"; changed.Category = PushNotificationCategory.Custom;
        await Assert.ThrowsAsync<ConflictException>(() => store.EnqueueAsync(changed, Ct));
    }

    [Fact]
    public async Task ReassigningDevice_SkipsPreviousOwnersQueueAndRejectsForeignRemoval()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var first = ProfileSocialStoreTests.NewUser("First");
        var second = ProfileSocialStoreTests.NewUser("Second");
        db.Users.AddRange(first, second); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        var installation = Guid.NewGuid();
        await store.RegisterDeviceAsync(first.Id, installation, "token", Ct);
        await store.EnqueueAsync(Notification(first.Id), Ct);
        await store.RegisterDeviceAsync(second.Id, installation, "token", Ct);
        await store.RemoveDeviceAsync(first.Id, installation, Ct);
        Assert.Equal(second.Id, (await db.PushDevices.SingleAsync(Ct)).UserId);
        Assert.Null(await store.ClaimAsync(Ct));
        Assert.Equal("Skipped", (await db.PushDeliveries.SingleAsync(Ct)).Outcome);
    }

    [Fact]
    public async Task RefreshingToken_PreservesQueueAndUnregisteredResponseCannotDeleteNewToken()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var user = ProfileSocialStoreTests.NewUser("Owner");
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        var installation = Guid.NewGuid();
        await store.RegisterDeviceAsync(user.Id, installation, "old-token", Ct);
        await store.EnqueueAsync(Notification(user.Id), Ct);
        await store.RegisterDeviceAsync(user.Id, installation, "refreshed-token", Ct);
        var dispatch = Assert.IsType<PushDispatch>(await store.ClaimAsync(Ct));
        Assert.Equal("refreshed-token", dispatch.Token);
        await store.RegisterDeviceAsync(user.Id, installation, "latest-token", Ct);
        await store.CompleteAsync(dispatch, PushSendResult.Unregistered, Ct);
        Assert.Equal("latest-token", (await db.PushDevices.SingleAsync(Ct)).Token);
    }

    [Fact]
    public async Task Delivery_RechecksPreferencesDisabledUserAndExpiry()
    {
        foreach (var change in new[] { "preferences", "blocked", "deleted", "expired" })
        {
            await using var db = ProfileSocialStoreTests.Db();
            var user = ProfileSocialStoreTests.NewUser("Owner");
            db.Users.Add(user); await db.SaveChangesAsync(Ct);
            var store = new PushNotificationStore(db, TimeProvider.System);
            await store.RegisterDeviceAsync(user.Id, Guid.NewGuid(), "token", Ct);
            var notification = Notification(user.Id);
            await store.EnqueueAsync(notification, Ct);
            if (change == "preferences") await store.SetPreferencesAsync(user.Id, new(Moderation: false), Ct);
            if (change == "blocked") user.SetBlocked(true);
            if (change == "deleted") user.SetSoftDelete();
            if (change == "expired") notification.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(Ct);
            Assert.Null(await store.ClaimAsync(Ct));
            Assert.Equal("Skipped", (await db.PushDeliveries.SingleAsync(Ct)).Outcome);
        }
    }

    [Fact]
    public async Task Inactivity_RequiresConsentAndBothKindsOfInactivityAndRespectsCooldown()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var user = ProfileSocialStoreTests.NewUser("Owner");
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        await store.RegisterDeviceAsync(user.Id, Guid.NewGuid(), "token", Ct);
        var settings = await db.NotificationPreferences.SingleAsync(Ct);
        settings.LastSeenAtUtc = DateTime.UtcNow.AddDays(-8);
        await db.SaveChangesAsync(Ct);
        await store.QueueRemindersAsync(7, 7, Ct);
        Assert.Empty(db.PushNotifications);
        settings.Inactivity = true;
        settings.LastCheckInAtUtc = DateTime.UtcNow.AddDays(-2);
        await db.SaveChangesAsync(Ct);
        await store.QueueRemindersAsync(7, 7, Ct);
        Assert.Empty(db.PushNotifications);
        settings.LastCheckInAtUtc = DateTime.UtcNow.AddDays(-8);
        await db.SaveChangesAsync(Ct);
        await store.QueueRemindersAsync(7, 7, Ct);
        await store.QueueRemindersAsync(7, 7, Ct);
        Assert.Single(db.PushNotifications);
        Assert.NotNull(settings.LastReminderAtUtc);
        settings.LastReminderAtUtc = DateTime.UtcNow.AddDays(-8);
        await db.SaveChangesAsync(Ct);
        await store.QueueRemindersAsync(7, 7, Ct);
        Assert.Equal(2, await db.PushNotifications.CountAsync(Ct));
        await store.RecordActivityAsync(user.Id, DateTime.UtcNow, false, Ct);
        Assert.Null(await store.ClaimAsync(Ct));
    }

    [Fact]
    public void Activity_IsMonotonicAndCheckInsDoNotPretendToBeAppVisits()
    {
        var now = DateTime.UtcNow;
        var settings = new NotificationPreferences { LastSeenAtUtc = now.AddDays(-10), Inactivity = true };
        settings.RecordActivity(now.AddDays(-2), true);
        settings.RecordActivity(now.AddDays(-5), true);
        settings.RecordActivity(now.AddDays(-11), false);
        Assert.Equal(now.AddDays(-2), settings.LastCheckInAtUtc);
        Assert.Equal(now.AddDays(-10), settings.LastSeenAtUtc);
        Assert.False(settings.IsInactive(now, 7, 7));
        Assert.Throws<ValidationException>(() => settings.RecordActivity(DateTime.SpecifyKind(now, DateTimeKind.Unspecified), true));
    }

    [Fact]
    public async Task Lease_RecoversAfterCrashAndIgnoresLateCompletionFromOldWorker()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var user = ProfileSocialStoreTests.NewUser("Owner");
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        await store.RegisterDeviceAsync(user.Id, Guid.NewGuid(), "token", Ct);
        await store.EnqueueAsync(Notification(user.Id), Ct);
        var first = Assert.IsType<PushDispatch>(await store.ClaimAsync(Ct));
        Assert.Null(await store.ClaimAsync(Ct));
        (await db.PushDeliveries.SingleAsync(Ct)).LeaseUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync(Ct);
        var second = Assert.IsType<PushDispatch>(await store.ClaimAsync(Ct));
        await store.CompleteAsync(first, PushSendResult.Accepted, Ct);
        Assert.Null((await db.PushDeliveries.SingleAsync(Ct)).FinishedAtUtc);
        await store.CompleteAsync(second, new("Retry", TimeSpan.FromHours(2)), Ct);
        Assert.True((await db.PushDeliveries.SingleAsync(Ct)).NextAttemptAtUtc > DateTime.UtcNow.AddMinutes(119));
    }

    private static PushNotification Notification(Guid userId, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), UserId = userId, Category = PushNotificationCategory.Moderation,
        Title = "Decision", Body = "Open the app", CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
    };
}
