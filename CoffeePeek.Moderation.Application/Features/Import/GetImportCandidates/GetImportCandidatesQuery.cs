using CoffeePeek.Contract.Enums;
using ContractBucket = CoffeePeek.Contract.Enums.ImportCollectorBucket;
using ContractStatus = CoffeePeek.Contract.Enums.ImportQueueStatus;
using ContractRejectReason = CoffeePeek.Contract.Enums.ImportRejectReason;
using ContractSource = CoffeePeek.Contract.Enums.ImportSource;

namespace CoffeePeek.Moderation.Application.Features.Import.GetImportCandidates;

public record GetImportCandidatesQuery(
    ContractStatus? Status = ContractStatus.Pending,
    ContractBucket? Bucket = null,
    CoffeeShopType? Type = null,
    ContractRejectReason? RejectReason = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20,
    ContractSource? Source = null,
    string? RequiredSignal = null);
