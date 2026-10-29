using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using ContractStatus = CoffeePeek.Contract.Enums.ImportQueueStatus;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Import.DecideImportCandidate;

public static class DecideImportCandidateHandler
{
    public static async Task<(Response<ShopImportCandidateDto>, object?)> Handle(
        DecideImportCandidateCommand command,
        IShopImportCandidateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var candidate = await repository.GetByIdAsync(command.Id, ct);
        if (candidate is null)
            return (Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.NotFound, "Import candidate not found."), null);

        try
        {
            if (command.Status == ContractStatus.Published)
                ImportOpeningHoursParser.Parse(candidate.OpeningHours);
            candidate.Decide(
                ShopImportCandidateMapper.ToDomain(command.Status),
                command.Type is null ? null : ShopImportCandidateMapper.ToDomain(command.Type.Value),
                command.TagSlugs,
                command.ReviewerUserId,
                command.OverrideClosed,
                DateTimeOffset.UtcNow,
                command.RejectReason is null
                    ? null
                    : ShopImportCandidateMapper.ToDomain(command.RejectReason.Value));
        }
        catch (DomainException ex)
        {
            return (Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.BadRequest, ex.Message), null);
        }

        await unitOfWork.SaveChangesAsync(ct);

        object? outbound = null;
        if (command.Status == ContractStatus.Published
            && candidate.CoffeeFocus.HasValue
            && candidate.ResultingShopId is null)
        {
            outbound = new ImportCandidatePublishedEvent(
            [
                ImportPublishFactory.FromCandidate(candidate, command.ReviewerUserId, command.OverrideClosed)
            ]);
        }

        return (Response<ShopImportCandidateDto>.Success(ShopImportCandidateMapper.ToDto(candidate)), outbound);
    }
}
