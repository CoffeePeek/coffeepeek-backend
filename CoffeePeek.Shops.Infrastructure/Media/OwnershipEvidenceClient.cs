using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos.Ownership;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Ownership;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Media;

public sealed class OwnershipEvidenceClient(IHttpClientFactory clients, IOptions<GatewayAuthOptions> auth) : IOwnershipEvidencePhotos
{
    private HttpRequestMessage Request(HttpMethod method, string path, Guid owner)
    {
        var request = new HttpRequestMessage(method, path);
        request.AddGatewayAuthHeader(auth.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, owner.ToString());
        return request;
    }
    public async Task AttachAsync(Guid applicantId, Guid claimId, Guid[] photoIds, CancellationToken ct,
        Guid? previousRejectedClaimId = null)
    {
        using var request = Request(HttpMethod.Post, "/internal/ownership-evidence/attach", applicantId);
        request.Content = JsonContent.Create(new AttachOwnershipEvidenceRequest(claimId, photoIds, previousRejectedClaimId));
        using var response = await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName).SendAsync(request, ct);
        if ((int)response.StatusCode >= 500) throw new ServiceUnavailableException("Evidence storage is unavailable.");
        if (!response.IsSuccessStatusCode) throw new ValidationException("Evidence photos are missing, expired or already attached.");
    }
    public async Task<HttpResponseMessage> DownloadAsync(Guid applicantId, Guid claimId, Guid photoId, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, $"/internal/ownership-evidence/{claimId}/{photoId}", applicantId);
        return await clients.CreateClient(MediaUploadedPhotoLookup.HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
}
