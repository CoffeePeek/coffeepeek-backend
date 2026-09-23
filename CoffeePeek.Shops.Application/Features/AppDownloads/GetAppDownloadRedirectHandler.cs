using System.Net;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class GetAppDownloadRedirectHandler
{
    public static async Task<Response<string>> Handle(
        GetAppDownloadRedirectQuery query,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var settings = await repository.GetOrCreateSettingsAsync(ct);
        var redirectUrl = query.Channel.ToLowerInvariant() switch
        {
            "android" when settings.AndroidGooglePlayEnabled && !string.IsNullOrWhiteSpace(settings.AndroidGooglePlayUrl) =>
                settings.AndroidGooglePlayUrl,
            "ios" when settings.IosAppStoreEnabled && !string.IsNullOrWhiteSpace(settings.IosAppStoreUrl) =>
                settings.IosAppStoreUrl,
            "apk" when settings.AndroidApkEnabled => (await repository.GetActiveAndroidReleaseAsync(ct))?.FileUrl,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(redirectUrl))
            return Response<string>.Error(HttpStatusCode.NotFound, "Download channel is not available.");

        repository.AddRedirectEvent(AppDownloadRedirectEvent.Create(
            query.Channel,
            query.UserAgent,
            query.Referer,
            query.Country));

        await unitOfWork.SaveChangesAsync(ct);
        return Response<string>.Success(redirectUrl);
    }
}
