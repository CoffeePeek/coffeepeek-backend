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
