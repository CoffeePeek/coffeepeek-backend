using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

namespace CoffeePeek.Moderation.Domain.Import;

public static class ImportDecisionMapper
{
    public static MappedImportDecision? FromSpike(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "yes" or "specialty" => new MappedImportDecision(
                ImportQueueStatus.Published, ImportCoffeeFocus.Specialty, []),
            "good_coffee" => new MappedImportDecision(
                ImportQueueStatus.Published, ImportCoffeeFocus.CoffeeBar, []),
            "cafe" => new MappedImportDecision(
                ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, []),
            "to_go" => new MappedImportDecision(
                ImportQueueStatus.Published, ImportCoffeeFocus.CoffeeBar, ["to_go"]),
            "no" or "reject" or "invalid" => new MappedImportDecision(
                ImportQueueStatus.Rejected, null, [], ImportRejectReason.Invalid),
            "closed" => new MappedImportDecision(
                ImportQueueStatus.Rejected, null, [], ImportRejectReason.Closed),
            "not_coffee" or "notcoffee" => new MappedImportDecision(
                ImportQueueStatus.Rejected, null, [], ImportRejectReason.NotCoffee),
            "skip" or "later" => new MappedImportDecision(
                ImportQueueStatus.Skipped, null, []),
            _ => null
        };
    }
}
