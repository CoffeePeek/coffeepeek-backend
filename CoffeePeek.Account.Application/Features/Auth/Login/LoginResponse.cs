using System.Text.Json.Serialization;

namespace CoffeePeek.Account.Application.Features.Auth.Login;

public record LoginResponse(
    string AccessToken,
    [property: JsonIgnore] string RefreshToken,
    DateTime AccessTokenExpiresAt);