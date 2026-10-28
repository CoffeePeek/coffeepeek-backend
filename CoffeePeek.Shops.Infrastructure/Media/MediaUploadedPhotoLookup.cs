using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Media;

/// <summary>
/// Calls MediaService directly (service discovery, not the gateway) as the check-in author:
/// the X-Gateway-Auth secret authenticates the service call, X-User-Id scopes the lookup
/// to that user's own uploads.
/// </summary>
public class MediaUploadedPhotoLookup(
    IHttpClientFactory httpClientFactory,
    IOptions<GatewayAuthOptions> gatewayAuthOptions,
    ILogger<MediaUploadedPhotoLookup> logger) : IUploadedPhotoLookup
{
    public const string HttpClientName = "media-photo-lookup";
    public const string ResolvePath = "/api/Photos/resolve";

    public async Task<ShopPhotoClaimResult> ClaimForShopAsync(Guid userId, Guid shopId,
        ShopPhotoAttachmentKind kind, IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty || shopId == Guid.Empty || storageKeys.Count == 0)
            return ShopPhotoClaimResult.Failed;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/shop-photos/claim");
        request.AddGatewayAuthHeader(gatewayAuthOptions.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, userId.ToString());
        request.Content = JsonContent.Create(new ClaimShopPhotosRequest(shopId, kind, storageKeys.ToArray()));
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var unavailable = (int)response.StatusCode >= 500;
                ShopsTelemetry.ShopPhotoClaimFailed(unavailable);
                logger.LogWarning("Shop photo claim returned {StatusCode} for {ShopId} ({Kind})",
                    (int)response.StatusCode, shopId, kind);
                return unavailable ? ShopPhotoClaimResult.Unreachable : ShopPhotoClaimResult.Failed;
            }
            var payload = await response.Content.ReadFromJsonAsync<Response<List<ClaimedShopPhotoDto>>>(cancellationToken: ct);
            if (payload is { IsSuccess: true, Data: not null })
                return new ShopPhotoClaimResult(true, payload.Data);
            ShopsTelemetry.ShopPhotoClaimFailed(false);
            logger.LogWarning("Shop photo claim returned an unsuccessful payload for {ShopId} ({Kind})", shopId, kind);
            return ShopPhotoClaimResult.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            ShopsTelemetry.ShopPhotoClaimFailed(true);
            logger.LogWarning(ex, "Shop photo claim failed for {ShopId} ({Kind})", shopId, kind);
            return ShopPhotoClaimResult.Unreachable;
        }
    }

    public async Task<CheckInPhotoClaimResult> ClaimForCheckInAsync(Guid userId, Guid checkInId,
        IReadOnlyCollection<string> storageKeys, CancellationToken ct = default, string? drinkPhotoStorageKey = null)
    {
        if (userId == Guid.Empty || checkInId == Guid.Empty || storageKeys.Count == 0)
            return CheckInPhotoClaimResult.Failed;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/check-in-photos/claim");
        request.AddGatewayAuthHeader(gatewayAuthOptions.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, userId.ToString());
        request.Content = JsonContent.Create(new ClaimCheckInPhotosRequest(checkInId, storageKeys.ToArray(), drinkPhotoStorageKey));
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var unavailable = (int)response.StatusCode >= 500;
                ShopsTelemetry.CheckInPhotoClaimFailed(unavailable);
                logger.LogWarning("Check-in photo claim returned {StatusCode} for {CheckInId}",
                    (int)response.StatusCode, checkInId);
                return CheckInPhotoClaimResult.Failed;
            }
            var payload = await response.Content.ReadFromJsonAsync<Response<List<ClaimedCheckInPhotoDto>>>(cancellationToken: ct);
            if (payload is { IsSuccess: true, Data: not null })
                return new CheckInPhotoClaimResult(true, payload.Data);
            ShopsTelemetry.CheckInPhotoClaimFailed(false);
            logger.LogWarning("Check-in photo claim returned an unsuccessful payload for {CheckInId}", checkInId);
            return CheckInPhotoClaimResult.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ShopsTelemetry.CheckInPhotoClaimFailed(true);
            logger.LogWarning(ex, "Check-in photo claim failed for {CheckInId}", checkInId);
            return CheckInPhotoClaimResult.Failed;
        }
    }

    public async Task<ClaimedCheckInPhotoDto?> CopyShelfBagCardAsync(Guid userId, Guid checkInId, Guid bagId,
        Guid cardPhotoId, Guid copyId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/check-in-photos/shelf-bag-card");
        request.AddGatewayAuthHeader(gatewayAuthOptions.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, userId.ToString());
        request.Content = JsonContent.Create(new CopyShelfBagCardRequest(checkInId, bagId, cardPhotoId, copyId));
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Shelf bag card copy returned {StatusCode} for {CheckInId}", (int)response.StatusCode, checkInId);
                return null;
            }
            var payload = await response.Content.ReadFromJsonAsync<Response<ClaimedCheckInPhotoDto>>(cancellationToken: ct);
            return payload is { IsSuccess: true, Data: not null } ? payload.Data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Shelf bag card copy failed for {CheckInId}", checkInId);
            return null;
        }
    }

    public async Task ReleaseCheckInClaimAsync(Guid userId, Guid checkInId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/internal/check-in-photos/{checkInId}/claim");
        request.AddGatewayAuthHeader(gatewayAuthOptions.Value.SecretKey);
        request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, userId.ToString());
        using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<UploadedPhotoLookupResult> ResolveAsync(
        Guid userId,
        IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty || storageKeys.Count == 0)
            return UploadedPhotoLookupResult.Failed;

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
                    ? UploadedPhotoLookupResult.Unreachable
                    : UploadedPhotoLookupResult.Failed;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<Response<List<ResolvedUploadedPhotoDto>>>(cancellationToken: ct);
            if (payload is not { IsSuccess: true })
            {
                logger.LogWarning(
                    "Media photo resolve for {UserId} was rejected: {Message}",
                    userId,
                    payload?.Message);
                return UploadedPhotoLookupResult.Failed;
            }

            return new UploadedPhotoLookupResult(true, payload.Data ?? []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Media photo resolve for {UserId} failed", userId);
            return UploadedPhotoLookupResult.Unreachable;
        }
    }
}
