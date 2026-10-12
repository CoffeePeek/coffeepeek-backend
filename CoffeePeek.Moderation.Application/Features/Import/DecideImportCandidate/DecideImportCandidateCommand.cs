using CoffeePeek.Contract.Enums;
using ContractStatus = CoffeePeek.Contract.Enums.ImportQueueStatus;
using ContractRejectReason = CoffeePeek.Contract.Enums.ImportRejectReason;

namespace CoffeePeek.Moderation.Application.Features.Import.DecideImportCandidate;

public record DecideImportCandidateCommand(
    Guid Id,
    ContractStatus Status,
    CoffeeShopType? Type,
    string[]? TagSlugs,
    bool OverrideClosed,
    Guid ReviewerUserId,
    ContractRejectReason? RejectReason = null);
