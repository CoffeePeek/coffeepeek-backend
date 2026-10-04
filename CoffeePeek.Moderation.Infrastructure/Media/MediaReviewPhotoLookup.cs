using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Moderation.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Infrastructure.Media;

/// <summary>
/// Calls MediaService directly (service discovery, not the gateway) as the review author:
/// the X-Gateway-Auth secret authenticates the service call, X-User-Id scopes the lookup
/// to that user's own uploads.
/// </summary>
public class MediaReviewPhotoLookup(
    IHttpClientFactory httpClientFactory,
    IOptions<GatewayAuthOptions> gatewayAuthOptions,
    ILogger<MediaReviewPhotoLookup> logger) : IReviewPhotoLookup
{
    public const string HttpClientName = "media-review-photo-lookup";
    public const string ResolvePath = "/api/Photos/resolve";

    public async Task<ReviewPhotoLookupResult> ResolveAsync(
        Guid userId,
        IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty || storageKeys.Count == 0)
            return ReviewPhotoLookupResult.Failed;

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, ResolvePath);
        request.AddGatewayAuthHeader(gatewayAuthOptions.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, userId.ToString());
        request.Content = JsonContent.Create(new { storageKeys });

        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Media photo resolve for {UserId} failed with {StatusCode}",
                    userId,
                    response.StatusCode);
                return (int)response.StatusCode >= 500
                    ? ReviewPhotoLookupResult.Unreachable
                    : ReviewPhotoLookupResult.Failed;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<Response<List<ResolvedUploadedPhotoDto>>>(cancellationToken: ct);
            if (payload is not { IsSuccess: true })
            {
                logger.LogWarning(
                    "Media photo resolve for {UserId} was rejected: {Message}",
                    userId,
                    payload?.Message);
                return ReviewPhotoLookupResult.Failed;
            }

            return new ReviewPhotoLookupResult(true, payload.Data ?? []);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Media photo resolve for {UserId} failed", userId);
            return ReviewPhotoLookupResult.Unreachable;
        }
    }
}
