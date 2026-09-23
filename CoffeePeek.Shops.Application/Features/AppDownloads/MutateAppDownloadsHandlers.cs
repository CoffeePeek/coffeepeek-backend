using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class UpdateGooglePlayDownloadHandler
{
    public static async Task<Response<AdminAppDownloadsDto>> Handle(
        UpdateGooglePlayDownloadCommand command,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var settings = await repository.GetOrCreateSettingsAsync(ct);
        var oldValue = $"enabled={settings.AndroidGooglePlayEnabled};url={settings.AndroidGooglePlayUrl}";

        settings.UpdateGooglePlay(command.Url, command.Enabled);
        repository.AddAuditLog(AppDistributionAuditLog.Create(
            command.UserId,
            "AppDistribution.GooglePlayUpdated",
            AppDistributionSettings.SingletonId,
            oldValue,
            $"enabled={settings.AndroidGooglePlayEnabled};url={settings.AndroidGooglePlayUrl}"));

        await unitOfWork.SaveChangesAsync(ct);

        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);
        return Response<AdminAppDownloadsDto>.Success(AppDownloadMappers.ToAdminDto(settings, activeRelease));
    }
}

public static class UpdateAppStoreDownloadHandler
{
    public static async Task<Response<AdminAppDownloadsDto>> Handle(
        UpdateAppStoreDownloadCommand command,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var settings = await repository.GetOrCreateSettingsAsync(ct);
        var oldValue = $"enabled={settings.IosAppStoreEnabled};url={settings.IosAppStoreUrl}";

        settings.UpdateAppStore(command.Url, command.Enabled);
        repository.AddAuditLog(AppDistributionAuditLog.Create(
            command.UserId,
            "AppDistribution.AppStoreUpdated",
            AppDistributionSettings.SingletonId,
            oldValue,
            $"enabled={settings.IosAppStoreEnabled};url={settings.IosAppStoreUrl}"));

        await unitOfWork.SaveChangesAsync(ct);

        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);
        return Response<AdminAppDownloadsDto>.Success(AppDownloadMappers.ToAdminDto(settings, activeRelease));
    }
}

public static class UpdateAndroidApkChannelHandler
{
    public static async Task<Response<AdminAppDownloadsDto>> Handle(
        UpdateAndroidApkChannelCommand command,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var settings = await repository.GetOrCreateSettingsAsync(ct);
        var oldValue = $"enabled={settings.AndroidApkEnabled}";

        settings.SetApkEnabled(command.Enabled);
        repository.AddAuditLog(AppDistributionAuditLog.Create(
            command.UserId,
            "AppDistribution.ApkChannelUpdated",
            AppDistributionSettings.SingletonId,
            oldValue,
            $"enabled={settings.AndroidApkEnabled}"));

        await unitOfWork.SaveChangesAsync(ct);

        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);
        return Response<AdminAppDownloadsDto>.Success(AppDownloadMappers.ToAdminDto(settings, activeRelease));
    }
}

public static class CreateAndroidReleaseHandler
{
    public static async Task<Response<AndroidReleaseDto>> Handle(
        CreateAndroidReleaseCommand command,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var release = AndroidRelease.Create(
            command.Version,
            command.VersionCode,
            command.FileUrl,
            command.FileName,
            command.FileSize,
            command.Sha256,
            command.ReleasedAt ?? DateTime.UtcNow);

        repository.AddAndroidRelease(release);
        repository.AddAuditLog(AppDistributionAuditLog.Create(
            command.UserId,
            "AppDistribution.AndroidReleaseCreated",
            release.Id,
            null,
            $"version={release.Version};versionCode={release.VersionCode};fileUrl={release.FileUrl}"));

        await unitOfWork.SaveChangesAsync(ct);
        return Response<AndroidReleaseDto>.Success(release.ToDto());
    }
}

public static class PublishAndroidReleaseHandler
{
    public static async Task<Response<AndroidReleaseDto>> Handle(
        PublishAndroidReleaseCommand command,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var target = await repository.GetAndroidReleaseByIdAsync(command.Id, ct);
        if (target is null)
            return Response<AndroidReleaseDto>.Error(HttpStatusCode.NotFound, "Android release not found.");

        var published = await repository.GetPublishedAndroidReleasesAsync(ct);
        var previousActive = published.Where(r => r.Id != target.Id).ToArray();
        foreach (var release in previousActive)
            release.Unpublish();

        if (previousActive.Length > 0)
            await unitOfWork.SaveChangesAsync(ct);

        target.Publish();
        repository.AddAuditLog(AppDistributionAuditLog.Create(
            command.UserId,
            "AppDistribution.AndroidReleasePublished",
            target.Id,
            string.Join(',', previousActive.Select(r => r.Id)),
            $"activeReleaseId={target.Id};version={target.Version}"));

        await unitOfWork.SaveChangesAsync(ct);
        return Response<AndroidReleaseDto>.Success(target.ToDto());
    }
}
