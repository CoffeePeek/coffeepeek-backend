namespace CoffeePeek.Account.Application.Features.Notifications;

public interface IPushSender
{
    Task<PushSendResult> SendAsync(PushDispatch dispatch, CancellationToken ct);
}
