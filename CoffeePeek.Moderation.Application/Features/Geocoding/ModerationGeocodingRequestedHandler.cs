using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Moderation.Domain.Aggregates;
using Microsoft.Extensions.Logging;
using Wolverine.Attributes;

namespace CoffeePeek.Moderation.Application.Features.Geocoding;

public static class ModerationGeocodingRequestedHandler
{
    // The only write is a single compare-and-update statement. Keep the transaction boundary
    // around that statement, not around the potentially slow external geocoding request.
    [NonTransactional]
    [RetryNow(typeof(HttpRequestException), 100, 500, 1000)]
    public static async Task Handle(
        ModerationGeocodingRequested request,
        IModerationShopRepository shopRepository,
        IModerationRoasterRepository roasterRepository,
        IAddressGeocoder geocodingService,
        ILogger<ModerationGeocodingRequested> logger,
        CancellationToken ct)
    {
        var isAwaiting = request.Kind switch
        {
            ModerationGeocodingKind.Shop => await shopRepository.IsAwaitingGeocodingAsync(
                request.EntityId, request.SubmittedAddress, ct),
            ModerationGeocodingKind.Roaster => await roasterRepository.IsAwaitingGeocodingAsync(
                request.EntityId, request.SubmittedAddress, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Kind), request.Kind, "Unknown moderation entity")
        };

        if (!isAwaiting)
        {
            logger.LogInformation("Skipping stale geocoding request for {Kind} {EntityId}", request.Kind, request.EntityId);
            return;
        }

        // A true no-result leaves the row unvalidated for manual review. Network and timeout
        // failures throw so the durable Wolverine queue can retry them.
        var coordinates = await geocodingService.GeocodeAsync(request.SubmittedAddress, ct);
        if (coordinates is null)
        {
            logger.LogWarning("No geocoding result for {Kind} {EntityId}; manual address review is required",
                request.Kind, request.EntityId);
            return;
        }

        var updated = request.Kind switch
        {
            ModerationGeocodingKind.Shop => await shopRepository.TryApplyGeocodingAsync(
                request.EntityId, request.SubmittedAddress, coordinates.Latitude, coordinates.Longitude, ct),
            ModerationGeocodingKind.Roaster => await roasterRepository.TryApplyGeocodingAsync(
                request.EntityId, request.SubmittedAddress, coordinates.Latitude, coordinates.Longitude, ct),
            _ => false
        };

        if (!updated)
            logger.LogInformation("Geocoding result for {Kind} {EntityId} was stale and was not applied",
                request.Kind, request.EntityId);
    }
}
