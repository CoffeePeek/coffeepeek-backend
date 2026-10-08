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
        var settings = await repository.GetSettingsOrDefaultAsync(ct);
        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);

        return Response<AppDownloadsDto>.Success(AppDownloadMappers.ToPublicDto(settings, activeRelease));
    }
}
