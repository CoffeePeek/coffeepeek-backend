using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public sealed class AppDownloadAnalyticsRetentionOptions
{
    [Range(1, 3650)]
    public int RetentionDays { get; init; } = 90;
}
