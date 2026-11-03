using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Media;

public sealed class MediaCollectionCoverClient(IHttpClientFactory clients, IOptions<GatewayAuthOptions> auth,
    ILogger<MediaCollectionCoverClient> logger) : ICollectionCoverStore
{
    public async Task<ClaimedCollectionCoverDto> ClaimAsync(Guid owner, Guid collectionId, string storageKey, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "/internal/collection-covers/claim", owner);
        request.Content = JsonContent.Create(new ClaimCollectionCoverRequest(collectionId, storageKey));
        try
        {
            using var response = await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName).SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new DiscoveryException("Cover is attached to another collection.", 409, "COLLECTION_CONFLICT");
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.Unauthorized)
                throw Unavailable();
            if (!response.IsSuccessStatusCode)
                throw new DiscoveryException("Cover upload was not found, owned or valid.", 400, "INVALID_COLLECTION");
            var payload = await response.Content.ReadFromJsonAsync<Response<ClaimedCollectionCoverDto>>(cancellationToken: ct);
            if (payload is not { IsSuccess: true, Data: { } photo } || photo.PhotoId == Guid.Empty
                || photo.SizeBytes is <= 0 or > 1048576 || photo.ContentType is not ("image/jpeg" or "image/png" or "image/webp")
                || !photo.StorageKey.StartsWith("collection-covers/sealed/", StringComparison.Ordinal))
                throw Unavailable();
            return photo;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                      || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Collection cover claim failed for {CollectionId}", collectionId);
            throw Unavailable();
        }
    }

    public async Task<HttpResponseMessage> DownloadAsync(CollectionCoverAccess photo, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, $"/internal/collection-covers/{photo.CollectionId}/{photo.PhotoId}", photo.OwnerId);
        return await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private HttpRequestMessage Request(HttpMethod method, string path, Guid owner)
    {
        var request = new HttpRequestMessage(method, path);
        request.AddGatewayAuthHeader(auth.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, owner.ToString());
        return request;
    }

    private static DiscoveryException Unavailable() => new("Cover storage is unavailable; retry the same upload.", 503, "COLLECTION_COVER_UNAVAILABLE");
}
