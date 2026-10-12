using CoffeePeek.Contract.Dtos.Menu;

namespace CoffeePeek.Moderation.Application.Features.Shop.Menu;

public record UpdateModerationShopMenuCommand(
    Guid ShopId,
    IReadOnlyList<UpdateShopMenuItemRequest> Items,
    bool ApplySuggestedPriceRange,
    Guid UserId);
