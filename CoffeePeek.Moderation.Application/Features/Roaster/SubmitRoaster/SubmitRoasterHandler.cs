using System.Net;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Common.Models;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Moderation.Application.Features.Roaster.SubmitRoaster;

public static class SubmitRoasterHandler
{
    public static async Task<Response<SubmitRoasterResponse>> Handle(
        SubmitRoasterCommand command,
        IModerationRoasterRepository repository,
        IRoasterExistenceLookup roasterExistenceLookup,
        IYandexGeocodingService geocodingService,
        IUnitOfWork unitOfWork,
        ILogger<SubmitRoasterCommand> logger,
        CancellationToken ct)
    {
        if (await roasterExistenceLookup.ExistsByNameAsync(command.Name, ct))
        {
            logger.LogWarning("Duplicate roaster submission detected: {Name}", command.Name);
            return Response<SubmitRoasterResponse>.Error(
                HttpStatusCode.Conflict, "A roaster with this name already exists.");
        }

        var roaster = ModerationRoaster.Create(command.Name, command.UserId, command.About);

        var isAddressValidated = false;
        if (command.CityId.HasValue && !string.IsNullOrWhiteSpace(command.Address))
        {
            var geocodingResult = await TryGeocodeAsync(command.Address, geocodingService, logger, ct);
            var location = geocodingResult is not null
                ? new ModerationLocation(command.Address, geocodingResult.Latitude, geocodingResult.Longitude)
                : new ModerationLocation(command.Address);
            isAddressValidated = geocodingResult is not null;
            roaster.SetLocation(command.CityId.Value, location);
        }

        if (!string.IsNullOrWhiteSpace(command.InstagramLink) || !string.IsNullOrWhiteSpace(command.SiteLink))
            roaster.UpdateContact(command.InstagramLink, command.SiteLink);

        if (command.Photos is { Count: > 0 })
        {
            foreach (var photo in command.Photos)
                roaster.AddPhoto(photo.FileName, photo.ContentType, photo.StorageKey, photo.Size);
        }

        await repository.AddAsync(roaster);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Roaster {RoasterId} submitted to moderation by user {UserId}", roaster.Id, command.UserId);

        var message = isAddressValidated || roaster.Location is null
            ? "The application has been accepted and will be reviewed by the moderator."
            : "The application has been accepted. Address coordinates could not be verified automatically and will be checked by a moderator.";

        return Response<SubmitRoasterResponse>.Success(
            new SubmitRoasterResponse(roaster.Id, "Pending", isAddressValidated),
            message);
    }

    private static async Task<GeocodingResult?> TryGeocodeAsync(
        string address,
        IYandexGeocodingService geocodingService,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            return await geocodingService.GeocodeAsync(address, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Geocoding service unavailable for address: {Address}", address);
            return null;
        }
    }
}
