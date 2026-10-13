namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface ISupportEmailSender
{
    Task SendAsync(string email, string message, CancellationToken ct);
}
