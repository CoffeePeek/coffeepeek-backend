using CoffeePeek.Account.Application.Features.Auth.AppleLogin;

namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface IAppleAuthService
{
    Task<AppleIdentity?> ValidateIdTokenAsync(string idToken, string nonce, CancellationToken ct);
}
