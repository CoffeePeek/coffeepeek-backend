using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Moderation.Application.Abstractions;
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
        ICityNameLookup cities,
        ILogger<ModerationGeocodingRequested> logger,
        CancellationToken ct)
    {
        var cityId = request.Kind switch
        {
            ModerationGeocodingKind.Shop => await shopRepository.GetAwaitingGeocodingCityIdAsync(
                request.EntityId, request.SubmittedAddress, ct),
            ModerationGeocodingKind.Roaster => await roasterRepository.GetAwaitingGeocodingCityIdAsync(
                request.EntityId, request.SubmittedAddress, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Kind), request.Kind, "Unknown moderation entity")
        };

        if (cityId is null)
        {
            logger.LogInformation("Skipping stale geocoding request for {Kind} {EntityId}", request.Kind, request.EntityId);
            return;
        }

        var cityName = await cities.GetNameAsync(cityId.Value, ct);
        if (string.IsNullOrWhiteSpace(cityName))
        {
            logger.LogWarning("City for {Kind} {EntityId} was not found; address review is required",
                request.Kind, request.EntityId);
            return;
        }

        // A true no-result leaves the row unvalidated for manual review. Network and timeout
        // failures throw so the durable Wolverine queue can retry them.
        var coordinates = await geocodingService.GeocodeAsync($"{cityName.Trim()}, {request.SubmittedAddress.Trim()}", ct);
        if (coordinates is null)
        {
            logger.LogWarning("No geocoding result for {Kind} {EntityId}; manual address review is required",
                request.Kind, request.EntityId);
            return;
        }

        var updated = request.Kind switch
        {
            ModerationGeocodingKind.Shop => await shopRepository.TryApplyGeocodingAsync(
                request.EntityId, request.SubmittedAddress, cityId.Value, coordinates.Latitude, coordinates.Longitude, ct),
            ModerationGeocodingKind.Roaster => await roasterRepository.TryApplyGeocodingAsync(
                request.EntityId, request.SubmittedAddress, cityId.Value, coordinates.Latitude, coordinates.Longitude, ct),
            _ => false
        };

        if (!updated)
            logger.LogInformation("Geocoding result for {Kind} {EntityId} was stale and was not applied",
                request.Kind, request.EntityId);
    }
}
