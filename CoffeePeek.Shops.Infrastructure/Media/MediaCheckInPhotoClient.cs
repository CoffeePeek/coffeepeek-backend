using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Media;

public class MediaCheckInPhotoClient(IHttpClientFactory clients, IOptions<GatewayAuthOptions> auth)
{
    public async Task<HttpResponseMessage> DownloadAsync(Guid ownerId, Guid checkInId, Guid photoId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/check-in-photos/{checkInId}/{photoId}");
        request.AddGatewayAuthHeader(auth.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, ownerId.ToString());
        return await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
}
