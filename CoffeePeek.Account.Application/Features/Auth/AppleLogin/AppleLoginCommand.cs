using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CoffeePeek.Account.Application.Features.Auth.AppleLogin;

public sealed record AppleLoginCommand(
    Guid ChallengeId,
    [property: Required, MaxLength(16384)] string IdToken,
    [property: JsonIgnore] string DeviceName = "unknown",
    [property: JsonIgnore] string IpAddress = "unknown");
