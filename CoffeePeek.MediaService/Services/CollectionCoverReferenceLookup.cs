using System.Net;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Services;

public sealed class CollectionCoverReferenceLookup(HttpClient client, IOptions<GatewayAuthOptions> auth) : ICollectionCoverReferenceLookup
{
    public async Task<bool?> IsCurrentAsync(Guid owner, Guid collectionId, Guid photoId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/collection-covers/{collectionId}/{photoId}");
        request.AddGatewayAuthHeader(auth.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, owner.ToString());
        try
        {
            using var response = await client.SendAsync(request, ct);
            return response.StatusCode switch { HttpStatusCode.NoContent => true, HttpStatusCode.NotFound => false, _ => null };
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        { return null; }
    }
}
