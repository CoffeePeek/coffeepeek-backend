using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class UpdateAppStoreDownloadHandler
{
    public static async Task<Response<AdminAppDownloadsDto>> Handle(
        UpdateAppStoreDownloadCommand command,
        IAppDistributionRepository repository,
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


        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);
        return Response<AdminAppDownloadsDto>.Success(AppDownloadMappers.ToAdminDto(settings, activeRelease));
    }
}
