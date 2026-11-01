namespace CoffeePeek.Account.Application.Common.Models;

public class AuthResult
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public required DateTime ExpiredAt { get; init; }
    public required DateTime RefreshTokenExpiresAt { get; init; }
}
