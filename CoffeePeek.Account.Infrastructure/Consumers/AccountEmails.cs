using System.Net;
using Microsoft.Extensions.Configuration;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Consumers;

internal static class AccountEmails
{
    public static string AccountLink(this IConfiguration config, string path, string token) =>
        $"{config["WebClientUrl"]}/{path}?token={Uri.EscapeDataString(token)}";

    public static async Task SendAccountEmailAsync(this IResend resend, string to, string template,
        string username, string url, CancellationToken ct)
    {
        var response = await resend.EmailSendAsync(new EmailMessage
        {
            To = to,
            Template = new EmailMessageTemplate
            {
                TemplateId = template,
                Variables = new Dictionary<string, object>
                {
                    // Resend substitutes these into raw HTML with triple braces.
                    ["USERNAME"] = WebUtility.HtmlEncode(username),
                    ["CONFIRMATION_URL"] = WebUtility.HtmlEncode(url),
                    ["YEAR"] = DateTime.UtcNow.Year
                }
            }
        }, ct);

        if (!response.Success)
        {
            if (response.Exception is { } error)
                throw error;
            throw new InvalidOperationException("Account email was not accepted by Resend.");
        }
    }
}
