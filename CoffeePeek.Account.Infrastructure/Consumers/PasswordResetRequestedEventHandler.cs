using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.Extensions.Configuration;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Consumers;

public class PasswordResetRequestedEventHandler(IResend resend, IConfiguration config)
{
    public Task Handle(PasswordResetRequestedInternalEvent @event, CancellationToken ct) =>
        resend.SendAccountEmailAsync(@event.Email, "reset-password", @event.Username,
            config.AccountLink("reset-password", @event.ResetToken), ct);
}
