namespace CoffeePeek.Account.Application.Features.Auth.AppleLogin;

public sealed record AppleLoginChallenge(Guid ChallengeId, string Nonce, DateTimeOffset ExpiresAt);
