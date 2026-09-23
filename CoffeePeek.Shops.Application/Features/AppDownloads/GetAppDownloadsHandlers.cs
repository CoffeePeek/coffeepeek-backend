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
