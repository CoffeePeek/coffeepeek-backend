using CoffeePeek.Account.Application.Common;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.Extensions.Configuration;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Consumers;

// Handler class names stay distinct: conventional routing names each RabbitMQ queue after its handler type.

public class UserRegisteredEventHandler(IResend resend, IConfiguration config)
{
    // Query param is read by the SPA; hash-only links remain supported on the frontend.
    public Task Handle(UserRegisteredInternalEvent @event, CancellationToken ct) =>
        resend.SendAccountEmailAsync(@event.Email, "Perfectly roasted beans are waiting for you! ☕",
            EmailTemplates.GetConfirmationHtml(@event.Username, config.AccountLink("confirm-email", @event.ConfirmationToken)), ct);
}

public class PasswordResetRequestedEventHandler(IResend resend, IConfiguration config)
{
    public Task Handle(PasswordResetRequestedInternalEvent @event, CancellationToken ct) =>
        resend.SendAccountEmailAsync(@event.Email, "Reset your CoffeePeek password ☕",
            EmailTemplates.GetPasswordResetHtml(@event.Username, config.AccountLink("reset-password", @event.ResetToken)), ct);
}

public class AccountDeletionRequestedEventHandler(IResend resend, IConfiguration config)
{
    public Task Handle(AccountDeletionRequestedInternalEvent @event, CancellationToken ct) =>
        resend.SendAccountEmailAsync(@event.Email, "Confirm your CoffeePeek account deletion",
            EmailTemplates.GetAccountDeletionConfirmationHtml(@event.Username,
                config.AccountLink("confirm-account-deletion", @event.ConfirmationToken)), ct);
}

internal static class AccountEmails
{
    public const string From = "CoffeePeek.by <info@coffeepeek.by>";

    public static string AccountLink(this IConfiguration config, string path, string token) =>
        $"{config["WebClientUrl"]}/{path}?token={Uri.EscapeDataString(token)}";

    // A failed send throws; Wolverine logs it and applies its retry policy.
    public static Task SendAccountEmailAsync(this IResend resend, string to, string subject, string html, CancellationToken ct) =>
        resend.EmailSendAsync(new EmailMessage { From = From, To = to, Subject = subject, HtmlBody = html }, ct);
}
