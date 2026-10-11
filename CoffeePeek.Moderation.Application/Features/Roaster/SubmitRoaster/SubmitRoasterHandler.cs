using System.Net;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Features.Geocoding;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Diagnostics;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Moderation.Application.Features.Roaster.SubmitRoaster;

public static class SubmitRoasterHandler
{
    public static async Task<(Response<SubmitRoasterResponse>, ModerationGeocodingRequested?, ModerationSubmissionCreated?)> Handle(
        SubmitRoasterCommand command,
        IModerationRoasterRepository repository,
        IRoasterExistenceLookup roasterExistenceLookup,
        IUnitOfWork unitOfWork,
        ILogger<SubmitRoasterCommand> logger,
        CancellationToken ct)
    {
        if (await WriteStageTrace.MeasureAsync("roaster_submit", "duplicate_validation",
                () => roasterExistenceLookup.ExistsByNameAsync(command.Name, ct)))
        {
            logger.LogWarning("Duplicate roaster submission detected: {Name}", command.Name);
            return (Response<SubmitRoasterResponse>.Error(
                HttpStatusCode.Conflict, "A roaster with this name already exists."), null, null);
        }

        var roaster = ModerationRoaster.Create(command.Name, command.UserId, command.About);

        ModerationGeocodingRequested? geocodingRequest = null;
        if (command.CityId.HasValue && !string.IsNullOrWhiteSpace(command.Address))
        {
            roaster.SetLocation(command.CityId.Value, new ModerationLocation(command.Address));
            geocodingRequest = new ModerationGeocodingRequested(
                ModerationGeocodingKind.Roaster, roaster.Id, command.Address);
        }

        if (!string.IsNullOrWhiteSpace(command.InstagramLink) || !string.IsNullOrWhiteSpace(command.SiteLink))
            roaster.UpdateContact(command.InstagramLink, command.SiteLink);

        if (command.Photos is { Count: > 0 })
        {
            foreach (var photo in command.Photos)
                roaster.AddPhoto(photo.FileName, photo.ContentType, photo.StorageKey, photo.Size);
        }

        await repository.AddAsync(roaster);
        await WriteStageTrace.MeasureAsync("roaster_submit", "db_save",
            () => unitOfWork.SaveChangesAsync(ct));

        logger.LogInformation("Roaster {RoasterId} submitted to moderation by user {UserId}", roaster.Id, command.UserId);

        var message = roaster.Location is null
            ? "The application has been accepted and will be reviewed by the moderator."
            : "The application has been accepted. Address verification is pending.";

        return (Response<SubmitRoasterResponse>.Success(
            new SubmitRoasterResponse(roaster.Id, "Pending", false),
            message), geocodingRequest,
            new ModerationSubmissionCreated(ModerationSubmissionKind.Roaster, roaster.Id, command.UserId,
                command.Name, Details: command.Address));
    }
}
