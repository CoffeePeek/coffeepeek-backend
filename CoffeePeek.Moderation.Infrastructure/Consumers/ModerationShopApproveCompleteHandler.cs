using CoffeePeek.Contract.Responses;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Moderation.Infrastructure.Consumers;

public class ModerationShopApproveCompleteHandler(
    IModerationShopRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<ModerationShopApproveCompleteHandler> logger)
{
    public async Task Handle(ModerationShopApproveCompleteResponse message, CancellationToken ct)
    {
        var moderationShop = await repository.GetByIdWithOutDetails(message.ModerationShopId, ct);

        if (moderationShop == null)
        {
            logger.LogWarning(
                "Moderation shop {ModerationShopId} not found when completing approve for published shop {ShopId}",
                message.ModerationShopId,
                message.ShopId);
            return;
        }

        if (moderationShop.ShopId == message.ShopId)
            return;

        moderationShop.AddShopId(message.ShopId);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
