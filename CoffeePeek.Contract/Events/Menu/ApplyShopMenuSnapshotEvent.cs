using CoffeePeek.Contract.Dtos.Menu;

namespace CoffeePeek.Contract.Events.Menu;

public record ApplyShopMenuSnapshotEvent(
    Guid ShopId,
    ShopMenuSnapshot Snapshot,
    bool ApplySuggestedPriceRange,
    Guid? UserId);
