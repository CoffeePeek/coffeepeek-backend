using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.ErrorCodes;
using CoffeePeek.Moderation.Application.Features.Geocoding;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Diagnostics;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Logging;
using Wolverine.Attributes;

namespace CoffeePeek.Moderation.Application.Features.Shop.CreateShop;

public static class SendCoffeeShopToModerationHandler
{
    public static async Task<(Response<SendCoffeeShopToModerationResponse>, ParseMenuRequestedEvent?, ModerationGeocodingRequested, ModerationSubmissionCreated)> Handle(
        SendCoffeeShopToModerationCommand command,
        IModerationShopRepository repository,
        IModerationShopCreationService creationService,
        ILogger<SendCoffeeShopToModerationCommand> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Processing moderation request for shop: {ShopName} by user: {UserId}",
            command.Name, command.UserId);

        var duplicateSpec = new DuplicateShopSpecification(command.Name, command.Address);
        if (await WriteStageTrace.MeasureAsync("shop_submit", "duplicate_validation",
                () => repository.Any(duplicateSpec, ct)))
        {
            logger.LogWarning("Duplicate shop detected: {Name} at {Address}",
                command.Name, command.Address);

            throw new ConflictException(
                "A coffee shop with that name and address is already pending moderation or has been approved.",
                ShopModerationErrorCodes.DuplicateShop);
        }

        var shopId = await WriteStageTrace.MeasureAsync("shop_submit", "db_save",
            () => creationService.Create(command, ct));

        var responseData = new SendCoffeeShopToModerationResponse(shopId, "Pending", false);

        logger.LogInformation("Shop {ShopId} successfully sent to moderation", shopId);

        const string message = "The application has been accepted. Address verification is pending.";

        ParseMenuRequestedEvent? parseEvent = null;
        if (command.MenuPhotos is { Count: > 0 })
        {
            var created = await repository.GetByIdAsync(shopId, ct);
            parseEvent = MenuParseEvents.FromDraft(
                MenuParseSourceKind.ModerationShop,
                shopId,
                null,
                created?.Menu,
                command.UserId);
        }

        return (Response<SendCoffeeShopToModerationResponse>.Success(responseData, message), parseEvent,
            new ModerationGeocodingRequested(ModerationGeocodingKind.Shop, shopId, command.Address),
            new ModerationSubmissionCreated(ModerationSubmissionKind.Shop, shopId, command.UserId,
                command.Name, Details: command.Address));
    }
}
