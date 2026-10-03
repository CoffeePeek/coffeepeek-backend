namespace CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

public static class CoffeeMapVerificationSignals
{
    public const string NeedsRecheck = "coffeemap:verification=needs-recheck";
    public const string RecheckReasonPrefix = "coffeemap:recheck-reason=";
}
