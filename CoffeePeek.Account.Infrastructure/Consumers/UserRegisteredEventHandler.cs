using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.Extensions.Configuration;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Consumers;

// Handler names determine the existing RabbitMQ queue names.
public class UserRegisteredEventHandler(IResend resend, IConfiguration config)
{
    public Task Handle(UserRegisteredInternalEvent @event, CancellationToken ct) =>
        resend.SendAccountEmailAsync(@event.Email, "confirm-email", @event.Username,
            config.AccountLink("confirm-email", @event.ConfirmationToken), ct);
}
