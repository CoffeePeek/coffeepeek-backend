using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Identity;

public class AccountDeletionTokenService(IOptions<AccountDeletionOptions> options) : IAccountDeletionTokenService
{
    private readonly byte[] _pepper = Encoding.UTF8.GetBytes(options.Value.TokenPepper);

    public (string Token, string Hash) Generate()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (token, Hash(token));
    }

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexString(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();
    }
}
