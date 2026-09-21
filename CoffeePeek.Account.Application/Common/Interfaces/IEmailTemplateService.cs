namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface IEmailTemplateService
{
    string GetConfirmationHtml(string username, string confirmationUrl);
    string GetPasswordResetHtml(string username, string resetUrl);
    string GetAccountDeletionConfirmationHtml(string username, string confirmationUrl);
    string GetWelcomeBackHtml(string username);
}
