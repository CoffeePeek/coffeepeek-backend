using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

public sealed class AppDistributionSettings : Entity<Guid>
{
    public static readonly Guid SingletonId = Guid.Parse("80f1b4fb-ae21-430f-aefd-6f3f672595bd");

    public string? AndroidGooglePlayUrl { get; private set; }
    public string? IosAppStoreUrl { get; private set; }
    public bool AndroidGooglePlayEnabled { get; private set; }
    public bool AndroidApkEnabled { get; private set; }
    public bool IosAppStoreEnabled { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private AppDistributionSettings()
    {
    }

    private AppDistributionSettings(Guid id)
    {
        Id = id;
    }

    public static AppDistributionSettings CreateDefault() => new AppDistributionSettings(SingletonId);

    public void UpdateGooglePlay(string? url, bool enabled)
    {
        var normalized = NormalizeOptionalUrl(url);
        if (enabled && normalized is null)
            throw new DomainException("Google Play URL is required when the channel is enabled.");

        if (normalized is not null)
            ValidateStoreUrl(normalized, "play.google.com", "Google Play URL");

        AndroidGooglePlayUrl = normalized;
        AndroidGooglePlayEnabled = enabled;
    }

    public void UpdateAppStore(string? url, bool enabled)
    {
        var normalized = NormalizeOptionalUrl(url);
        if (enabled && normalized is null)
            throw new DomainException("App Store URL is required when the channel is enabled.");

        if (normalized is not null)
            ValidateStoreUrl(normalized, "apps.apple.com", "App Store URL");

        IosAppStoreUrl = normalized;
        IosAppStoreEnabled = enabled;
    }

    public void SetApkEnabled(bool enabled) => AndroidApkEnabled = enabled;

    private static string? NormalizeOptionalUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.Trim();

    private static void ValidateStoreUrl(string url, string expectedHost, string fieldName)
    {
        ValidateHttpsUrl(url, fieldName);

        var uri = new Uri(url, UriKind.Absolute);
        if (!string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"{fieldName} must use {expectedHost}.");
    }

    public static void ValidateHttpsUrl(string url, string fieldName)
    {
        if (url.Length > BusinessConstants.MaxDownloadUrlLength)
            throw new DomainException($"{fieldName} cannot be longer than {BusinessConstants.MaxDownloadUrlLength} characters.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new DomainException($"{fieldName} must be a valid HTTPS URL.");
    }
}
