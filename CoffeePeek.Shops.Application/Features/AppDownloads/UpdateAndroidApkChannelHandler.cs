using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

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
