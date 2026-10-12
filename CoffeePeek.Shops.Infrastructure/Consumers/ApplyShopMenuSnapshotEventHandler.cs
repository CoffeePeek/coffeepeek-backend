using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class ApplyShopMenuSnapshotEventHandler(
    IApplyShopMenuService applyMenu,
    IShopMenuRepository menus,
    IUnitOfWork unitOfWork,
    ILogger<ApplyShopMenuSnapshotEventHandler> logger)
{
    public async Task Handle(ApplyShopMenuSnapshotEvent message, CancellationToken ct)
    {
        if (message.ExpectedInputRevision is null or < 0)
        {
            logger.LogWarning("Menu snapshot for shop {ShopId} requires manual reconciliation: no valid expected revision", message.ShopId);
            return;
        }
        var menu = await menus.GetTrackedByShopIdAsync(message.ShopId, ct);
        if (message.ExpectedInputRevision != (menu?.InputRevision ?? 0L))
        {
            logger.LogWarning("Ignored stale menu snapshot for shop {ShopId}: expected {ExpectedRevision}, current {CurrentRevision}",
                message.ShopId, message.ExpectedInputRevision, menu?.InputRevision ?? 0L);
            return;
        }
        try
        {
            await applyMenu.ApplySnapshotAsync(
                message.ShopId,
                message.Snapshot,
                message.ApplySuggestedPriceRange,
                message.UserId,
                ct);
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
