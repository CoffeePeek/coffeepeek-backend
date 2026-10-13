namespace CoffeePeek.Account.Application.Features.Auth.AppleLogin;

public sealed record AppleIdentity(string Subject, string? Email, bool EmailVerified);
