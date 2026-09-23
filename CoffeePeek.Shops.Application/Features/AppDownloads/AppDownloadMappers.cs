using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

internal static class AppDownloadMappers
{
    public static AppDownloadsDto ToPublicDto(AppDistributionSettings settings, AndroidRelease? activeRelease) =>
        new AppDownloadsDto(
            new AndroidDownloadsDto(
                new DownloadChannelDto(
                    settings.AndroidGooglePlayEnabled,
                    settings.AndroidGooglePlayEnabled && !string.IsNullOrWhiteSpace(settings.AndroidGooglePlayUrl),
                    AppDownloadRoutes.AndroidUrl),
                new ApkDownloadChannelDto(
                    settings.AndroidApkEnabled,
                    settings.AndroidApkEnabled && activeRelease is not null,
                    AppDownloadRoutes.ApkUrl,
                    activeRelease?.Version,
                    activeRelease?.VersionCode,
                    activeRelease?.FileName,
                    activeRelease?.FileSize,
                    activeRelease?.Sha256,
                    activeRelease?.ReleasedAt)),
            new IosDownloadsDto(
                new DownloadChannelDto(
                    settings.IosAppStoreEnabled,
                    settings.IosAppStoreEnabled && !string.IsNullOrWhiteSpace(settings.IosAppStoreUrl),
                    AppDownloadRoutes.IosUrl)));

    public static AdminAppDownloadsDto ToAdminDto(AppDistributionSettings settings, AndroidRelease? activeRelease) =>
        new AdminAppDownloadsDto(
            new AdminStoreChannelDto(
                settings.AndroidGooglePlayEnabled,
                settings.AndroidGooglePlayEnabled && !string.IsNullOrWhiteSpace(settings.AndroidGooglePlayUrl),
                AppDownloadRoutes.AndroidUrl,
                settings.AndroidGooglePlayUrl),
            new AdminApkChannelDto(
                settings.AndroidApkEnabled,
                settings.AndroidApkEnabled && activeRelease is not null,
                AppDownloadRoutes.ApkUrl,
                activeRelease?.ToDto()),
            new AdminStoreChannelDto(
                settings.IosAppStoreEnabled,
                settings.IosAppStoreEnabled && !string.IsNullOrWhiteSpace(settings.IosAppStoreUrl),
                AppDownloadRoutes.IosUrl,
                settings.IosAppStoreUrl),
            settings.UpdatedAtUtc ?? settings.CreatedAtUtc);

    public static AndroidReleaseDto ToDto(this AndroidRelease release) =>
        new AndroidReleaseDto(
            release.Id,
            release.Version,
            release.VersionCode,
            release.FileUrl,
            release.FileName,
            release.FileSize,
            release.Sha256,
            release.ReleasedAt,
            release.IsActive,
            release.CreatedAtUtc,
            release.UpdatedAtUtc);
}
