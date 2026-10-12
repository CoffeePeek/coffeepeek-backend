using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

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
