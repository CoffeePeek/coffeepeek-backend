using CoffeePeek.Contract.Events.Moderation;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace CoffeePeek.Moderation.Infrastructure.Notifications;

public static class ModerationSubmissionCreatedHandler
{
    public static void Configure(HandlerChain chain) =>
        chain.OnException(e => e is HttpRequestException or TaskCanceledException)
            .ScheduleRetry(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));

    public static Task Handle(ModerationSubmissionCreated message, TelegramModerationClient telegram,
        CancellationToken ct) => telegram.SendAsync(message, ct);
}
