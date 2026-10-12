namespace CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

public sealed record ImportCandidateStats(
    int Pending,
    int Skipped,
    int Published,
    int Rejected,
    int InFeed,
    IReadOnlyDictionary<ImportCoffeeFocus, int> ByFocus,
    IReadOnlyDictionary<ImportCollectorBucket, int> ByBucket,
    ImportRejectedByReasonStats RejectedByReason);
