using CoffeePeek.Account.Application.Common.Models;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;

namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface IAppleWebAuthService
{
    Task<AppleAuthorizationRequest> CreateAuthorizationAsync(CancellationToken ct);
    Task<AppleLoginCommand?> ExchangeCodeAsync(string? code, string state, CancellationToken ct);
}
