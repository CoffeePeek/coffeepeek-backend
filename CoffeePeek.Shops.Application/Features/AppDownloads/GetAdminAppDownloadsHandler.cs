using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class GetAdminAppDownloadsHandler
{
    public static async Task<Response<AdminAppDownloadsDto>> Handle(
        GetAdminAppDownloadsQuery _,
        IAppDistributionRepository repository,
        CancellationToken ct)
    {
        var settings = await repository.GetSettingsOrDefaultAsync(ct);
        var activeRelease = await repository.GetActiveAndroidReleaseAsync(ct);

        return Response<AdminAppDownloadsDto>.Success(AppDownloadMappers.ToAdminDto(settings, activeRelease));
    }
}
