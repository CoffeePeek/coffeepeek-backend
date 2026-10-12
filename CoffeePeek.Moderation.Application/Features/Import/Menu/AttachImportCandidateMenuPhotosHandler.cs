using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Application.Features.Import.Menu;

public static class AttachImportCandidateMenuPhotosHandler
{
    public static async Task<(Response<ShopImportCandidateDto>, ParseMenuRequestedEvent?)> Handle(
        AttachImportCandidateMenuPhotosCommand command,
        IShopImportCandidateRepository repository,
        IUnitOfWork unitOfWork,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var candidate = await repository.GetByIdAsync(command.CandidateId, ct);
        if (candidate is null)
            return (Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.NotFound, "Import candidate not found."), null);

        candidate.AttachMenuPhotos(
            command.Photos.Select(p => (p.FileName, p.ContentType, p.StorageKey, p.Size)).ToArray(),
            DateTime.UtcNow);

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
