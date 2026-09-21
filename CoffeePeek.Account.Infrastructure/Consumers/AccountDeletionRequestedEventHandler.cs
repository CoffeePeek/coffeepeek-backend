using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Consumers;

public class AccountDeletionRequestedEventHandler(
    IResend resend,
    IConfiguration config,
    IEmailTemplateService templateService,
    ILogger<AccountDeletionRequestedEventHandler> logger)
{
    public async Task Handle(AccountDeletionRequestedInternalEvent @event, CancellationToken ct)
    {
        var confirmationUrl =
            $"{config["WebClientUrl"]}/confirm-account-deletion?token={Uri.EscapeDataString(@event.ConfirmationToken)}";

        var message = new EmailMessage
        {
            From = "CoffeePeek.by <info@coffeepeek.by>",
            To = @event.Email,
            Subject = "Confirm your CoffeePeek account deletion",
            HtmlBody = templateService.GetAccountDeletionConfirmationHtml(@event.Username, confirmationUrl)
        };

        try
        {
            await resend.EmailSendAsync(message, ct);
            logger.LogInformation(
                "Account deletion confirmation email sent for request {RequestId}",
                @event.RequestId);
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "Account deletion confirmation email failed for request {RequestId}",
                @event.RequestId);
            throw;
        }
    }
}
