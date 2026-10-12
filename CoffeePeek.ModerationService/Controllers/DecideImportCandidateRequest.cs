using CoffeePeek.Contract.Enums;

namespace CoffeePeek.ModerationService.Controllers;

public record DecideImportCandidateRequest(
    ImportQueueStatus Status,
    CoffeeShopType? Type,
    string[]? TagSlugs,
    bool OverrideClosed = false,
    ImportRejectReason? RejectReason = null);
