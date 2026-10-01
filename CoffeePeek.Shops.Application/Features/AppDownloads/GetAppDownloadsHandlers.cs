using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class GetPublicAppDownloadsHandler
{
    public static async Task<Response<AppDownloadsDto>> Handle(
        GetPublicAppDownloadsQuery _,
        IAppDistributionRepository repository,
        CancellationToken ct)
    {
        var settings = await repository.GetOrCreateSettingsAsync(ct);
        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);

        return Response<AppDownloadsDto>.Success(AppDownloadMappers.ToPublicDto(settings, activeRelease));
    }
}

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

        var settings = await repository.GetOrCreateSettingsAsync(ct);
        var (latest, min, url) = platform == "android"
            ? await GetAndroidPolicyAsync(settings, channel, repository, ct)
            : (settings.IosLatestVersionCode, settings.IosMinVersionCode, settings.IosAppStoreEnabled ? settings.IosAppStoreUrl : null);

        if (latest == 0 || string.IsNullOrWhiteSpace(url))
            return Response<AppVersionDto>.Error(HttpStatusCode.NotFound, "App version is not configured for this platform.");

        // A floor above the downloadable build would trap clients in an update loop, so it only bites once that build ships.
        return Response<AppVersionDto>.Success(new AppVersionDto(latest, Math.Min(min, latest), url));
    }

    // versionCode and url always come from the same channel: Play uses the admin-set build, APK the active release.
    // No channel = Play when available, else APK.
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

public static class GetAdminAppDownloadsHandler
{
    public static async Task<Response<AdminAppDownloadsDto>> Handle(
        GetAdminAppDownloadsQuery _,
        IAppDistributionRepository repository,
        CancellationToken ct)
    {
        var settings = await repository.GetOrCreateSettingsAsync(ct);
        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);

        return Response<AdminAppDownloadsDto>.Success(AppDownloadMappers.ToAdminDto(settings, activeRelease));
    }
}

public static class GetAndroidReleasesHandler
{
    public static async Task<Response<AndroidReleaseDto[]>> Handle(
        GetAndroidReleasesQuery _,
        IAppDistributionRepository repository,
        CancellationToken ct)
    {
        var releases = await repository.GetAndroidReleasesAsync(ct);
        return Response<AndroidReleaseDto[]>.Success(releases.Select(r => r.ToDto()).ToArray());
    }
}

public static class GetAndroidReleaseByIdHandler
{
    public static async Task<Response<AndroidReleaseDto>> Handle(
        GetAndroidReleaseByIdQuery query,
        IAppDistributionRepository repository,
        CancellationToken ct)
    {
        var release = await repository.GetAndroidReleaseByIdAsync(query.Id, ct);
        return release is null
            ? Response<AndroidReleaseDto>.Error(HttpStatusCode.NotFound, "Android release not found.")
            : Response<AndroidReleaseDto>.Success(release.ToDto());
    }
}
