using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Services;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class ApplyShopMenuSnapshotEventHandler(
    IApplyShopMenuService applyMenu,
    IUnitOfWork unitOfWork,
    ILogger<ApplyShopMenuSnapshotEventHandler> logger)
{
    public async Task Handle(ApplyShopMenuSnapshotEvent message, CancellationToken ct)
    {
        try
        {
            await applyMenu.ApplySnapshotAsync(
                message.ShopId,
                message.Snapshot,
                message.ApplySuggestedPriceRange,
                message.UserId,
                ct,
                message.PreserveManualItems);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Applied menu snapshot to shop {ShopId}", message.ShopId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply menu snapshot to shop {ShopId}", message.ShopId);
            throw;
        }
    }
}
