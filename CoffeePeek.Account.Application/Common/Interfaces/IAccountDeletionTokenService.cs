namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface IAccountDeletionTokenService
{
    (string Token, string Hash) Generate();
    string Hash(string token);
}
