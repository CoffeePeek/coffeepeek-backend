using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Shared.Auth.Options;

public class JWTOptions
{
    [Required]
    [MinLength(32)]
    public string SecretKey { get; set; } = null!;

    [Required]
    public string Issuer { get; set; } = null!;

    [Required]
    public string Audience { get; set; } = null!;

    [Range(1, 1440)]
    public int AccessTokenLifetimeMinutes { get; set; }

    [Range(1, 365)]
    public int RefreshTokenLifetimeDays { get; set; } = 90;

    [Range(1, 365)]
    public int RefreshTokenAbsoluteLifetimeDays { get; set; } = 365;
}
