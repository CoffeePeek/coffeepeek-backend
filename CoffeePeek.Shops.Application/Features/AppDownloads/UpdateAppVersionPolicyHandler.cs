using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class UpdateAppVersionPolicyHandler
{
    public static async Task<Response<AdminAppDownloadsDto>> Handle(
        UpdateAppVersionPolicyCommand command,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var platform = command.Platform.Trim().ToLowerInvariant();
        if (platform is not ("android" or "ios"))
            return Response<AdminAppDownloadsDto>.Error(HttpStatusCode.BadRequest, "Platform must be 'android' or 'ios'.");

        var settings = await repository.GetOrCreateSettingsAsync(ct);
        var oldValue = DescribeVersionPolicy(settings, platform);

        if (platform == "android")
            settings.UpdateAndroidVersionPolicy(command.LatestVersionCode, command.MinVersionCode);
        else
            settings.UpdateIosVersionPolicy(command.LatestVersionCode, command.MinVersionCode);

        repository.AddAuditLog(AppDistributionAuditLog.Create(
            command.UserId,
            "AppDistribution.VersionPolicyUpdated",
            AppDistributionSettings.SingletonId,
            oldValue,
            DescribeVersionPolicy(settings, platform)));

        await unitOfWork.SaveChangesAsync(ct);

        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);
        return Response<AdminAppDownloadsDto>.Success(AppDownloadMappers.ToAdminDto(settings, activeRelease));
    }

    private static string DescribeVersionPolicy(AppDistributionSettings settings, string platform) =>
        platform == "android"
            ? $"platform=android;latest={settings.AndroidLatestVersionCode};min={settings.AndroidMinVersionCode}"
            : $"platform=ios;latest={settings.IosLatestVersionCode};min={settings.IosMinVersionCode}";
}
