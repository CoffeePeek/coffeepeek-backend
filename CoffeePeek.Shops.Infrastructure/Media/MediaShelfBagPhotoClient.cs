using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Shelf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Media;

/// <summary>Calls Media's internal shelf-bag photo endpoints as the bag owner (X-Gateway-Auth + X-User-Id).</summary>
public class MediaShelfBagPhotoClient(IHttpClientFactory clients, IOptions<GatewayAuthOptions> auth,
    ILogger<MediaShelfBagPhotoClient> logger) : IShelfBagPhotoStore
{
    public async Task<ShelfBagPhotoClaimResult> ClaimAsync(Guid userId, Guid bagId, string storageKey, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "/internal/shelf-bag-photos/claim", userId);
        request.Content = JsonContent.Create(new ClaimShelfBagPhotoRequest(bagId, storageKey));
        try
        {
            using var response = await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName).SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Conflict) return new(ShelfBagPhotoClaimStatus.AttachedElsewhere);
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Shelf bag photo claim returned {StatusCode} for {BagId}", (int)response.StatusCode, bagId);
                return new(ShelfBagPhotoClaimStatus.Unavailable);
            }
            if (!response.IsSuccessStatusCode) return new(ShelfBagPhotoClaimStatus.Rejected);
            var payload = await response.Content.ReadFromJsonAsync<Response<ClaimedShelfBagPhotoDto>>(cancellationToken: ct);
            return payload is { IsSuccess: true, Data: { } photo }
                ? new(ShelfBagPhotoClaimStatus.Claimed, photo)
                : new(ShelfBagPhotoClaimStatus.Rejected);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Shelf bag photo claim failed for {BagId}", bagId);
            return new(ShelfBagPhotoClaimStatus.Unavailable);
        }
    }

    public async Task<HttpResponseMessage> DownloadAsync(Guid ownerId, Guid bagId, Guid photoId, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, $"/internal/shelf-bag-photos/{bagId}/{photoId}", ownerId);
        return await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private HttpRequestMessage Request(HttpMethod method, string path, Guid userId)
    {
        var request = new HttpRequestMessage(method, path);
        request.AddGatewayAuthHeader(auth.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, userId.ToString());
        return request;
    }
}
