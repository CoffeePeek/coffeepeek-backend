using CoffeePeek.Contract.Events.Moderation;

namespace CoffeePeek.Moderation.Infrastructure.Notifications;

public static class ModerationSubmissionCreatedHandler
{
    public static Task Handle(ModerationSubmissionCreated message, TelegramModerationClient telegram,
        CancellationToken ct) => telegram.SendAsync(message, ct);
}
