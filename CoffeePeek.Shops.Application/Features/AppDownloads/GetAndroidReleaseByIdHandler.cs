using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

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
