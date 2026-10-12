using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Import.GetImportDossierHints;

public static class GetImportDossierHintsHandler
{
    public static Task<Response<ImportDossierHintsDto>> Handle(
        GetImportDossierHintsQuery _,
        CancellationToken ct) =>
        Task.FromResult(Response<ImportDossierHintsDto>.Success(ShopImportCandidateMapper.DossierHints()));
}
