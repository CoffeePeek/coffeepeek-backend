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
        Assert.Equal(2, (await store.GetTemplatesAsync(Ct)).Count);
    }

    [Theory]
    [InlineData("Unknown {user}", "Body", PushNotificationCategory.Moderation)]
    [InlineData("Title", "{status}", PushNotificationCategory.Inactivity)]
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
}
