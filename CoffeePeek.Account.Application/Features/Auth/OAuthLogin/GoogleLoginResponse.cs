using System.Text.Json.Serialization;

namespace CoffeePeek.Account.Application.Features.Auth.OAuthLogin;

public record GoogleLoginResponse(
    string AccessToken,
    [property: JsonIgnore] string RefreshToken,
    DateTime AccessTokenExpiresAt,
    GoogleLoginUser User);
