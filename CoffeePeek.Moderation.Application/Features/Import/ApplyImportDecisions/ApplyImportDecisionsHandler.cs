using System.Text.Json;
using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Features.Import.DecideImportCandidate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Import;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Import.ApplyImportDecisions;

public record ApplyImportDecisionsCommand(
    Dictionary<string, string>? Decisions,
    JsonElement? Raw,
    bool OverrideClosed,
    Guid ReviewerUserId);

public static class ApplyImportDecisionsHandler
{
    public static async Task<(Response<ApplyImportDecisionsResultDto>, object?)> Handle(
        ApplyImportDecisionsCommand command,
        IShopImportCandidateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var decisions = ExtractDecisions(command);
        if (decisions.Count == 0)
        {
            return (Response<ApplyImportDecisionsResultDto>.Error(
                System.Net.HttpStatusCode.BadRequest,
                "No decisions provided."), null);
        }

        var keys = decisions.Keys.ToArray();
        var existing = new Dictionary<string, ShopImportCandidate>(StringComparer.Ordinal);
        foreach (var source in new[] { ImportSource.Osm, ImportSource.File })
        {
            foreach (var pair in await repository.GetByExternalIdsAsync(source, keys, ct))
                existing[pair.Key] = pair.Value;
        }
        var now = DateTimeOffset.UtcNow;
        var applied = 0;
        var published = 0;
        var rejected = 0;
        var skipped = 0;
        var unknown = 0;
        var missing = 0;
        var failed = 0;
        var publishItems = new List<ImportCandidatePublishedItem>();

        foreach (var (externalId, raw) in decisions)
        {
            var mapped = ImportDecisionMapper.FromSpike(raw);
            if (mapped is null)
            {
                unknown++;
                continue;
            }

            if (!existing.TryGetValue(externalId, out var candidate))
            {
                missing++;
                continue;
            }

            try
            {
                candidate.Decide(
                    mapped.Value.Status,
                    mapped.Value.Focus,
                    mapped.Value.TagSlugs,
                    command.ReviewerUserId,
                    command.OverrideClosed,
                    now,
                    mapped.Value.RejectReason);
            }
            catch (DomainException)
            {
                failed++;
                continue;
            }

            applied++;
            switch (mapped.Value.Status)
            {
                case Domain.Aggregates.ShopImportCandidateAggregate.ImportQueueStatus.Published:
                    published++;
                    publishItems.Add(ImportPublishFactory.FromCandidate(
                        candidate, command.ReviewerUserId, command.OverrideClosed));
                    break;
                case ImportQueueStatus.Rejected:
                    rejected++;
                    break;
                case ImportQueueStatus.Skipped:
                    skipped++;
                    break;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        object? outbound = publishItems.Count == 0
            ? null
            : new ImportCandidatePublishedEvent(publishItems);

        return (Response<ApplyImportDecisionsResultDto>.Success(
            new ApplyImportDecisionsResultDto(applied, published, rejected, skipped, unknown, missing, failed)), outbound);
    }

    private static Dictionary<string, string> ExtractDecisions(ApplyImportDecisionsCommand command)
    {
        if (command.Decisions is { Count: > 0 })
            return command.Decisions;

        if (command.Raw is not { ValueKind: JsonValueKind.Object } raw)
            return [];

        if (raw.TryGetProperty("decisions", out var nested) && nested.ValueKind == JsonValueKind.Object)
            return ReadStringMap(nested);

        return ReadStringMap(raw);
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement element)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                map[property.Name] = property.Value.GetString() ?? "";
        }

        return map;
    }
}
