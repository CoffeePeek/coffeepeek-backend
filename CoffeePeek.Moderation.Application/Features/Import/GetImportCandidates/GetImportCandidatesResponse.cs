using CoffeePeek.Contract.Dtos.Import;

namespace CoffeePeek.Moderation.Application.Features.Import.GetImportCandidates;

public record GetImportCandidatesResponse(
    IReadOnlyList<ShopImportCandidateDto> Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);
