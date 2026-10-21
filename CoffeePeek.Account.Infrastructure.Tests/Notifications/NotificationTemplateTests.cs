using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Account.Infrastructure.Notifications;
using CoffeePeek.Account.Infrastructure.Tests.Persistence;
using CoffeePeek.Account.Persistence.Notifications;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.Notifications;

public sealed class NotificationTemplateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavedTemplateIsUsedForNewDecisionsAndKeepsAlreadyQueuedMessages()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var user = ProfileSocialStoreTests.NewUser("TemplateOwner");
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        var handler = new ModerationDecisionChangedHandler(store, TimeProvider.System);
        var decision = new ModerationDecisionChanged(Guid.NewGuid(), user.Id, ModerationSubmissionKind.CheckIn, Guid.NewGuid(), "Approved");
        await handler.Handle(decision, Ct);
        await store.SetTemplateAsync(new PushNotificationTemplate
        {
            Category = PushNotificationCategory.Moderation, Title = "Решение: {status}",
            Body = "Проверка {subject} завершена: {status}.", UpdatedByUserId = user.Id
        }, Ct);
        await handler.Handle(decision, Ct);
        await handler.Handle(decision with { EventId = Guid.NewGuid() }, Ct);
        var messages = await db.PushNotifications.OrderBy(x => x.CreatedAtUtc).ToArrayAsync(Ct);
        Assert.Equal("Решение модерации", messages[0].Title);
        Assert.Equal("Решение: одобрено", messages[1].Title);
        Assert.Equal("Проверка чекина завершена: одобрено.", messages[1].Body);
        Assert.Equal(3, (await store.GetTemplatesAsync(Ct)).Count);
    }

    [Theory]
    [InlineData("Unknown {user}", "Body", PushNotificationCategory.Moderation)]
    [InlineData("Title", "{status}", PushNotificationCategory.Inactivity)]
    [InlineData("Title", "{status}", PushNotificationCategory.NewShop)]
    [InlineData("{name}", "Body", PushNotificationCategory.NewShop)]
    [InlineData("Title", "Body", PushNotificationCategory.Custom)]
    [InlineData(" ", "Body", PushNotificationCategory.Moderation)]
    public void InvalidTemplatesAreRejected(string title, string body, PushNotificationCategory category) =>
        Assert.Throws<ValidationException>(() => NotificationTemplateRenderer.Validate(new() { Title = title, Body = body, Category = category }));

    [Fact]
    public void LimitsApplyAfterExpansion() => Assert.Throws<ValidationException>(() =>
        NotificationTemplateRenderer.Validate(new()
        {
            Category = PushNotificationCategory.Moderation, Title = new string('x', 111) + "{subject}", Body = "Body"
        }));

    [Fact]
    public async Task NewShopUsesSavedTemplateAndReplayPreservesExistingMessages()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var user = ProfileSocialStoreTests.NewUser("ShopTemplateOwner");
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var store = new PushNotificationStore(db, TimeProvider.System);
        var shopId = Guid.NewGuid();
        var publishedAt = DateTime.UtcNow;
        await store.QueueNewShopBatchAsync(shopId, "Кофе {и} друзья", publishedAt, null, Ct);
        var originalBody = (await db.PushNotifications.SingleAsync(Ct)).Body;
        Assert.Contains("Кофе {и} друзья", originalBody);
        await store.SetTemplateAsync(new()
        {
            Category = PushNotificationCategory.NewShop, Title = "Новое место",
            Body = "Попробуйте кофе в «{name}»!", UpdatedByUserId = user.Id
        }, Ct);
        await store.QueueNewShopBatchAsync(shopId, "Кофе {и} друзья", publishedAt, null, Ct);
        Assert.Equal(originalBody, (await db.PushNotifications.SingleAsync(Ct)).Body);
        var nextShopId = Guid.NewGuid();
        await store.QueueNewShopBatchAsync(nextShopId, "Кофе {и} друзья", publishedAt, null, Ct);
        var next = await db.PushNotifications.SingleAsync(x => x.EntityId == nextShopId, Ct);
        Assert.Equal("Новое место", next.Title);
        Assert.Equal("Попробуйте кофе в «Кофе {и} друзья»!", next.Body);
    }
}
