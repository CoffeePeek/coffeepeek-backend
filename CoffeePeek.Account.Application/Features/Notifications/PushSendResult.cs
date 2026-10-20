namespace CoffeePeek.Account.Application.Features.Notifications;

public sealed record PushSendResult(string Outcome, TimeSpan? RetryAfter = null)
{
    public static readonly PushSendResult Accepted = new("Accepted");
    public static readonly PushSendResult Unregistered = new("Unregistered");
}
