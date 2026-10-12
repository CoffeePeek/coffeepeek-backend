using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Application.Features.Import.Menu;

public static class ParseImportCandidateMenuHandler
{
    public static async Task<(Response<ShopImportCandidateDto>, ParseMenuRequestedEvent?)> Handle(
        ParseImportCandidateMenuCommand command,
        IShopImportCandidateRepository repository,
        IUnitOfWork unitOfWork,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var candidate = await repository.GetByIdAsync(command.CandidateId, ct);
        if (candidate is null)
            return (Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.NotFound, "Import candidate not found."), null);

        try
        {
            candidate.RequestMenuParse();
        }
        catch (DomainException ex)
        {
            return (Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.BadRequest, ex.Message), null);
        }

        await unitOfWork.SaveChangesAsync(ct);

        return (
            Response<ShopImportCandidateDto>.Success(ShopImportCandidateMapper.ToDto(candidate, mediaOptions.Value)),
            MenuParseEvents.FromDraft(
                MenuParseSourceKind.ImportCandidate,
                candidate.Id,
                candidate.ResultingShopId,
                candidate.Menu,
                command.UserId));
    }
}
