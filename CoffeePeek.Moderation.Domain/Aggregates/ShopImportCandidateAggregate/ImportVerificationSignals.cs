namespace CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

public static class ImportVerificationSignals
{
    public const string NeedsRecheck = "import:verification=needs-recheck";
    public const string RecheckReasonPrefix = "import:recheck-reason=";
}
