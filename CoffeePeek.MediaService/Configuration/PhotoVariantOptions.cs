using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.MediaService.Configuration;

public sealed class PhotoVariantOptions
{
    // Capturing durable work remains enabled while execution is paused.
    public bool Enabled { get; set; }
    public bool ReadEnabled { get; set; }
    public string ReadAccessKey { get; set; } = "";
    public string ReadSecretKey { get; set; } = "";
    public bool CleanupEnabled { get; set; }
    public bool CleanupDryRun { get; set; } = true;
    public string RendererEndpoint { get; set; } = "http://checkin-renderer:8080";
    public string SigningKey { get; set; } = "";
    public string SigningSalt { get; set; } = "";
    public string BucketName { get; set; } = "coffeepeek.checkin-variants";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    [Range(1, 10)] public int MaxAttempts { get; set; } = 5;
    [Range(24, 720)] public int CleanupGraceHours { get; set; } = 24;
    public const int MaxSourceBytes = 32 * 1024 * 1024;
    public const int MaxResultBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(3);

    public bool IsValid() => (!ReadEnabled || (!string.IsNullOrWhiteSpace(ReadAccessKey)
        && !string.IsNullOrWhiteSpace(ReadSecretKey) && ReadAccessKey != AccessKey && !string.IsNullOrWhiteSpace(BucketName)))
        && ((!Enabled && !CleanupEnabled) ||
        (Uri.TryCreate(RendererEndpoint, UriKind.Absolute, out var uri)
         && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
         && !string.IsNullOrWhiteSpace(BucketName) && !string.IsNullOrWhiteSpace(AccessKey)
         && !string.IsNullOrWhiteSpace(SecretKey)
         && (!Enabled || (ValidHex(SigningKey) && ValidHex(SigningSalt)))));

    private static bool ValidHex(string value) => value.Length >= 64 && value.Length % 2 == 0
        && value.All(Uri.IsHexDigit);
}
