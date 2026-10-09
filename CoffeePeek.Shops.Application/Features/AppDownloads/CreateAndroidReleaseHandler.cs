using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class CreateAndroidReleaseHandler
{
    public static async Task<Response<AndroidReleaseDto>> Handle(
        CreateAndroidReleaseCommand command,
        IAppDistributionRepository repository,
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

        return Response<AndroidReleaseDto>.Success(release.ToDto());
    }
}
