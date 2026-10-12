using CoffeePeek.Contract.Dtos.Menu;

namespace CoffeePeek.Moderation.Application.Features.Import.Menu;

public record UpdateImportCandidateMenuCommand(
    Guid CandidateId,
    IReadOnlyList<UpdateShopMenuItemRequest> Items,
    bool ApplySuggestedPriceRange,
    Guid UserId);
