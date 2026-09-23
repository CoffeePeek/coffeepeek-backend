using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Shared.Auth.Options;

public class GatewayAuthOptions
{
    public const string SectionName = "GatewayAuth";

    /// <summary>
    /// Publicly known key committed in appsettings.json for local runs. Rejected outside Development.
    /// </summary>
    public const string DevelopmentSecretKey = "CoffeePeek-Gateway-Internal-Auth-Dev-Secret-Key-2026";

    [Required]
    [MinLength(32)]
    public string SecretKey { get; set; } = null!;

    public static bool IsAllowedIn(GatewayAuthOptions options, bool isDevelopment) =>
        isDevelopment || options.SecretKey != DevelopmentSecretKey;
}
