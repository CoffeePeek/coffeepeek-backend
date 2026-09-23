namespace CoffeePeek.AccountService.Controllers.Contracts;

public sealed record NativeRefreshTokenRequest(string RefreshToken);

public sealed record NativeLogoutRequest(string RefreshToken);

public sealed record NativeTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt);
