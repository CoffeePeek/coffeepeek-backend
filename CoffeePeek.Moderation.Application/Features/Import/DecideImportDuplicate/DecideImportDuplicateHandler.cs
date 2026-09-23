using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Import;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Import.DecideImportDuplicate;

public record DecideImportDuplicateCommand(Guid Id, bool Accept, Guid ReviewerUserId);

public static class DecideImportDuplicateHandler
{
    public static async Task<(Response<DecideImportDuplicateResultDto>, object?)> Handle(
        DecideImportDuplicateCommand command,
        IShopImportDuplicateSuggestionRepository suggestions,
        IShopImportCandidateRepository candidates,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var suggestion = await suggestions.GetByIdAsync(command.Id, ct);
        if (suggestion is null)
        {
            return (Response<DecideImportDuplicateResultDto>.Error(
                System.Net.HttpStatusCode.NotFound, "Duplicate suggestion not found."), null);
        }

        var byId = await candidates.GetByIdsAsync(
            [suggestion.LeftCandidateId, suggestion.RightCandidateId], ct);
        if (!byId.TryGetValue(suggestion.LeftCandidateId, out var left)
            || !byId.TryGetValue(suggestion.RightCandidateId, out var right))
        {
            return (Response<DecideImportDuplicateResultDto>.Error(
                System.Net.HttpStatusCode.NotFound, "One of the candidates is missing."), null);
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            if (!command.Accept)
            {
                suggestion.Reject(command.ReviewerUserId, now);
                await unitOfWork.SaveChangesAsync(ct);
                return (Response<DecideImportDuplicateResultDto>.Success(
                    new DecideImportDuplicateResultDto(
                        suggestion.Id, suggestion.Status.ToString(), null, null)), null);
            }

            if (left.QueueStatus == ImportQueueStatus.Published
                && right.QueueStatus == ImportQueueStatus.Published)
            {
                return (Response<DecideImportDuplicateResultDto>.Error(
                    System.Net.HttpStatusCode.BadRequest,
                    "Both candidates are already published as different shops. Unpublish one first."), null);
            }

            var keeper = ImportDuplicateScanner.PickKeeper(left, right);
            var duplicate = keeper.Id == left.Id ? right : left;

            keeper.EnrichFrom(duplicate.ToSnapshot(), now, duplicate.GoogleMapsUri);
            if (duplicate.ImportedFromFile)
                keeper.AddSignal("import:file");
            keeper.AddSignal("import:merged");

            duplicate.Decide(
                ImportQueueStatus.Rejected,
                focus: null,
                tagSlugs: null,
                command.ReviewerUserId,
                overrideClosed: false,
                now,
                ImportRejectReason.Duplicate);
            duplicate.AddSignal($"import:duplicate-of:{keeper.Id:N}");

            suggestion.Confirm(command.ReviewerUserId, now);
            await unitOfWork.SaveChangesAsync(ct);

            object? outbound = keeper.ResultingShopId is null
                ? null
                : new ImportShopEnrichmentEvent(
                [
                    new ImportShopEnrichmentItem(
                        keeper.ResultingShopId,
                        keeper.HasRealName ? keeper.Name!.Trim() : duplicate.Name ?? keeper.Name ?? "",
                        keeper.Address,
                        keeper.Latitude,
                        keeper.Longitude,
                        keeper.Phone,
                        keeper.Website,
                        keeper.Instagram)
                ]);

            return (Response<DecideImportDuplicateResultDto>.Success(
                new DecideImportDuplicateResultDto(
                    suggestion.Id,
                    suggestion.Status.ToString(),
                    keeper.Id,
                    duplicate.Id)), outbound);
        }
        catch (DomainException ex)
        {
            return (Response<DecideImportDuplicateResultDto>.Error(
                System.Net.HttpStatusCode.BadRequest, ex.Message), null);
        }
    }
}
