using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

namespace CoffeePeek.Moderation.Domain.Import;

public readonly record struct MappedImportDecision(
    ImportQueueStatus Status,
    ImportCoffeeFocus? Focus,
    IReadOnlyList<string> TagSlugs,
    ImportRejectReason? RejectReason = null);
