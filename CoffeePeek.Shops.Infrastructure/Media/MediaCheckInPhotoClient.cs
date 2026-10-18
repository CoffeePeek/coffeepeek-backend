using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Media;

public class MediaCheckInPhotoClient(IHttpClientFactory clients, IOptions<GatewayAuthOptions> auth)
{
    public async Task<HttpResponseMessage> DownloadVariantAsync(Guid ownerId, Guid checkInId, Guid photoId,
        string source, int recipe, string purpose, string format, bool head, string? ifNoneMatch, CancellationToken ct)
    {
        if (!CoffeePeek.Shared.Kernel.Media.CheckInPhotoVariantIdentity.IsAddress(source, recipe, purpose, format))
            throw new ArgumentException("Invalid photo variant address.");
        using var request = new HttpRequestMessage(head ? HttpMethod.Head : HttpMethod.Get,
            $"/internal/check-in-photo-variants/{checkInId}/{photoId}/{source}/{recipe}/{purpose}.{format}");
        request.AddGatewayAuthHeader(auth.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, ownerId.ToString());
        if (!string.IsNullOrEmpty(ifNoneMatch)) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        return await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public async Task<HttpResponseMessage> DownloadAsync(Guid ownerId, Guid checkInId, Guid photoId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/check-in-photos/{checkInId}/{photoId}");
        request.AddGatewayAuthHeader(auth.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, ownerId.ToString());
        return await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
}
