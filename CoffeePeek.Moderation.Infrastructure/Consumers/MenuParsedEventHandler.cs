using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Moderation.Infrastructure.Consumers;

public class MenuParsedEventHandler(
    IShopImportCandidateRepository candidateRepository,
    IModerationShopRepository moderationShopRepository,
    IUnitOfWork unitOfWork,
    ILogger<MenuParsedEventHandler> logger)
{
    public async Task<ApplyShopMenuSnapshotEvent?> Handle(MenuParsedEvent message, CancellationToken ct)
    {
        MenuDraft? menu;
        Guid? publishedShopId;
        switch (message.SourceKind)
        {
            case MenuParseSourceKind.ImportCandidate:
                var candidate = await candidateRepository.GetByIdAsync(message.SourceId, ct);
                menu = candidate?.Menu;
                publishedShopId = candidate?.ResultingShopId;
                break;
            case MenuParseSourceKind.ModerationShop:
                var shop = await moderationShopRepository.GetByIdAsync(message.SourceId, ct);
                menu = shop?.Menu;
                publishedShopId = shop?.ShopId;
                break;
            default:
                return null;
        }

        if (menu is null || !menu.ApplyParseResult(message.Success, message.Error,
                message.SuggestedPriceRange is null ? null : (int)message.SuggestedPriceRange.Value,
                MenuDraftMapper.ToDraftItems(message.Items), MenuDraftMapper.ToDraftUnmatched(message.Unmatched),
                message.CapturedAtUtc, message.ParseRequestId))
        {
            logger.LogInformation("Skipping stale menu parse result for {Kind} {SourceId}", message.SourceKind, message.SourceId);
            return null;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return message.Success && publishedShopId.HasValue && publishedShopId != Guid.Empty
            ? new ApplyShopMenuSnapshotEvent(publishedShopId.Value, MenuDraftMapper.ToSnapshot(menu)!, false, null,
                PreserveManualItems: true)
            : null;
    }
}
