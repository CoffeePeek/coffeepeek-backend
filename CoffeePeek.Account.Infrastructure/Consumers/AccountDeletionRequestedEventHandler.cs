using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using Microsoft.Extensions.Configuration;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Consumers;

public class AccountDeletionRequestedEventHandler(IResend resend, IConfiguration config)
{
    public Task Handle(AccountDeletionRequestedInternalEvent @event, CancellationToken ct) =>
        resend.SendAccountEmailAsync(@event.Email, "confirm-account-deletion", @event.Username,
            config.AccountLink("confirm-account-deletion", @event.ConfirmationToken), ct);
}
