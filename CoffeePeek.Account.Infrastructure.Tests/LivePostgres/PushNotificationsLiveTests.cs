using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Account.Persistence.Notifications;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class PushNotificationsLiveTests : IClassFixture<AccountLiveDatabase>
{
    private readonly AccountLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PushNotificationsLiveTests(AccountLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task MigratedPostgresSerializesRegistrationAndEnqueueAndFencesConcurrentClaims()
    {
        var user = await fixture.RegisterAsync("Push" + Guid.NewGuid().ToString("N")[..8], false, Ct);
        var installation = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = fixture.Context(false);
            await new PushNotificationStore(db, TimeProvider.System).RegisterDeviceAsync(user, installation, "test-token", Ct);
        }));
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = fixture.Context(false);
            await new PushNotificationStore(db, TimeProvider.System).EnqueueAsync(new PushNotification
            {
                Id = eventId, UserId = user, Title = "Title", Body = "Body", Category = PushNotificationCategory.Custom,
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
            }, Ct);
        }));
        var claims = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = fixture.Context(false);
            return await new PushNotificationStore(db, TimeProvider.System).ClaimAsync(Ct);
        }));
        var dispatch = Assert.Single(claims.OfType<PushDispatch>());
        await using (var db = fixture.Context(false))
        {
            var store = new PushNotificationStore(db, TimeProvider.System);
            Assert.Equal(1, await db.PushDevices.CountAsync(Ct));
            Assert.Equal(1, await db.PushNotifications.CountAsync(Ct));
            await store.CompleteAsync(dispatch, PushSendResult.Accepted, Ct);
            Assert.Equal("Accepted", (await db.PushDeliveries.SingleAsync(Ct)).Outcome);
            await store.SetPreferencesAsync(user, new(Inactivity: true), Ct);
            var preferences = await db.NotificationPreferences.SingleAsync(Ct);
            preferences.LastSeenAtUtc = DateTime.UtcNow.AddDays(-8);
            preferences.LastCheckInAtUtc = DateTime.UtcNow.AddDays(-8);
            await db.SaveChangesAsync(Ct);
            await store.SetTemplateAsync(new()
            {
                Category = PushNotificationCategory.Inactivity, Title = "Новый заголовок", Body = "Новый текст", UpdatedByUserId = user
            }, Ct);
        }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = fixture.Context(false);
            await new PushNotificationStore(db, TimeProvider.System).QueueRemindersAsync(7, 7, Ct);
        }));
        await using var verify = fixture.Context(false);
        Assert.Equal(1, await verify.PushNotifications.CountAsync(x => x.Category == PushNotificationCategory.Inactivity, Ct));
        var reminder = await verify.PushNotifications.SingleAsync(x => x.Category == PushNotificationCategory.Inactivity, Ct);
        Assert.Equal("Новый текст", reminder.Body);
        reminder.ExpiresAtUtc = DateTime.UtcNow.AddDays(-31);
        await verify.SaveChangesAsync(Ct);
        await new PushNotificationStore(verify, TimeProvider.System).PurgeAsync(Ct);
        Assert.False(await verify.PushNotifications.AnyAsync(x => x.Id == reminder.Id, Ct));
        verify.ChangeTracker.Clear();
        verify.Users.Remove(await verify.Users.SingleAsync(x => x.Id == user, Ct));
        await verify.SaveChangesAsync(Ct);
        Assert.Empty(await verify.PushDevices.ToArrayAsync(Ct));
        Assert.Empty(await verify.PushDeliveries.ToArrayAsync(Ct));
    }
}
