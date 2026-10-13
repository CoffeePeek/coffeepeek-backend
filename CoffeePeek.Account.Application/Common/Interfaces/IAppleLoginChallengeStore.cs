using CoffeePeek.Account.Application.Features.Auth.AppleLogin;

namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface IAppleLoginChallengeStore
{
    Task<AppleLoginChallenge> CreateAsync(CancellationToken ct);
    Task<string?> GetNonceAsync(Guid challengeId, CancellationToken ct);
    Task<bool> ConsumeAsync(Guid challengeId, string nonce, CancellationToken ct);
}
