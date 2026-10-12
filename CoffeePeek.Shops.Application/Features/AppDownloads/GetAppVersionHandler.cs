using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class GetAppVersionHandler
{
    public static async Task<Response<AppVersionDto>> Handle(
        GetAppVersionQuery query,
        IAppDistributionRepository repository,
        CancellationToken ct)
    {
        var platform = query.Platform?.Trim().ToLowerInvariant();
        if (platform is not ("android" or "ios"))
            return Response<AppVersionDto>.Error(HttpStatusCode.BadRequest, "Platform must be 'android' or 'ios'.");

        var channel = query.Channel?.Trim().ToLowerInvariant();
        if (channel is not (null or "play" or "apk") || (channel is not null && platform == "ios"))
            return Response<AppVersionDto>.Error(HttpStatusCode.BadRequest, "Channel must be 'play' or 'apk' and is only supported on android.");

        var settings = await repository.GetSettingsOrDefaultAsync(ct);
        var (latest, min, url) = platform == "android"
            ? await GetAndroidPolicyAsync(settings, channel, repository, ct)
            : (settings.IosLatestVersionCode, settings.IosMinVersionCode, settings.IosAppStoreEnabled ? settings.IosAppStoreUrl : null);

        if (latest == 0 || string.IsNullOrWhiteSpace(url))
            return Response<AppVersionDto>.Error(HttpStatusCode.NotFound, "App version is not configured for this platform.");

        return Response<AppVersionDto>.Success(new AppVersionDto(latest, Math.Min(min, latest), url));
    }

    private static async Task<(int Latest, int Min, string? Url)> GetAndroidPolicyAsync(
        AppDistributionSettings settings,
        string? channel,
        IAppDistributionRepository repository,
        CancellationToken ct)
    {
        var playAvailable = settings.AndroidGooglePlayEnabled && !string.IsNullOrWhiteSpace(settings.AndroidGooglePlayUrl);
        if (channel == "play" || (channel is null && playAvailable))
            return (settings.AndroidLatestVersionCode, settings.AndroidMinVersionCode, playAvailable ? settings.AndroidGooglePlayUrl : null);

        var release = settings.AndroidApkEnabled ? await repository.GetActiveAndroidReleaseAsync(ct) : null;
        return (release?.VersionCode ?? 0, settings.AndroidMinVersionCode, release?.FileUrl);
    }
}
