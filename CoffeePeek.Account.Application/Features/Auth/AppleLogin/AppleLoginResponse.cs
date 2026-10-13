namespace CoffeePeek.Account.Application.Features.Auth.AppleLogin;

// Native clients persist both tokens in their protected session store.
public sealed record AppleLoginResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt);
