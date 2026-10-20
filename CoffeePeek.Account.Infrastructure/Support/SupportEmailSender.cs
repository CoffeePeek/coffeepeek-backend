using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Infrastructure.Consumers;
using CoffeePeek.Account.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Support;

public sealed class SupportEmailSender(IResend resend, IOptions<SupportOptions> options) : ISupportEmailSender
{
    public async Task SendAsync(string email, string message, CancellationToken ct)
    {
        var response = await resend.EmailSendAsync(new EmailMessage
        {
            From = AccountEmails.From,
            To = options.Value.Email,
            ReplyTo = email,
            Subject = "CoffeePeek support request",
            TextBody = $"Reply to: {email}\n\n{message}"
        }, ct);
        if (!response.Success)
        {
            if (response.Exception is { } error)
                throw error;
            throw new InvalidOperationException("Support email was not accepted by Resend.");
        }
    }
}
