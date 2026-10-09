using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Responses;
using CoffeePeek.Shops.Application.Services;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class ModerationShopApproveHandler(
    ICreateShopFromModerationService createShopService,
    ILogger<ModerationShopApproveHandler> logger)
{
    public async Task<ModerationShopApproveCompleteResponse> Handle(
        ModerationShopApprovedEvent message,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Received ModerationShopApprovedEvent for moderation shop {ModerationShopId} ({ShopName})",
            message.Shop.Id,
            message.Shop.Name);

        var shopId = await createShopService.CreateShopFromApprovedEventAsync(
            message.Shop,
            creatorId: message.UserId,
            moderationId: message.Shop.Id,
            ct);

        logger.LogInformation(
            "Prepared published shop {ShopId} from moderation shop {ModerationShopId} for transaction commit",
            shopId,
            message.Shop.Id);

        return new ModerationShopApproveCompleteResponse(message.Shop.Id, shopId);
    }
}
