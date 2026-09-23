using System.Text.Json.Serialization;

namespace CoffeePeek.Account.Application.Features.Auth.RefreshToken;

public record RefreshTokenResponse(
    string AccessToken,
    [property: JsonIgnore] string RefreshToken,
    DateTime AccessTokenExpiresAt);
