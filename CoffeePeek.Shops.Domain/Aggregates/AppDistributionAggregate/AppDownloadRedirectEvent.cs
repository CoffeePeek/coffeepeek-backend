using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

public sealed class AppDownloadRedirectEvent : Entity<Guid>
{
    public string Channel { get; private set; } = null!;
    public string? UserAgent { get; private set; }
    public string? Referer { get; private set; }
    public string? Country { get; private set; }
    public DateTime Timestamp { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private AppDownloadRedirectEvent()
    {
    }

    private AppDownloadRedirectEvent(Guid id, string channel, string? userAgent, string? referer, string? country)
    {
        Id = id;
        Channel = TrimToLimit(channel, BusinessConstants.MaxRedirectChannelLength) ?? channel;
        UserAgent = TrimToLimit(userAgent, BusinessConstants.MaxRedirectUserAgentLength);
        Referer = TrimToLimit(referer, BusinessConstants.MaxRedirectRefererLength);
        Country = TrimToLimit(country, BusinessConstants.MaxRedirectCountryLength);
        Timestamp = DateTime.UtcNow;
    }

    public static AppDownloadRedirectEvent Create(string channel, string? userAgent, string? referer, string? country) =>
        new AppDownloadRedirectEvent(Guid.NewGuid(), channel, userAgent, referer, country);

    public static AppDownloadRedirectEvent Create(Guid id, string channel, string? userAgent, string? referer, string? country) =>
        new AppDownloadRedirectEvent(id, channel, userAgent, referer, country);

    private static string? TrimToLimit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
