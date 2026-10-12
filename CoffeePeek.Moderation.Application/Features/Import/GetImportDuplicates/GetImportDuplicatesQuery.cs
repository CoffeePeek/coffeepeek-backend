using ContractStatus = CoffeePeek.Contract.Enums.ImportDuplicateStatus;

namespace CoffeePeek.Moderation.Application.Features.Import.GetImportDuplicates;

public record GetImportDuplicatesQuery(
    ContractStatus? Status = ContractStatus.Pending,
    int Page = 1,
    int PageSize = 20);
