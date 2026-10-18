namespace CoffeePeek.Contract.Dtos.CheckIns;

/// <summary>Operator-only report. Storage addresses and credentials are never returned.</summary>
public sealed record PhotoBackfillInspection(Guid PhotoId, string? EligibilityFailure, string SourceStatus,
    long DeclaredBytes, long? MediaDeclaredBytes, long? ActualBytes, bool MetadataMismatch,
    string? JobStatus, long? CurrentVariantBytes, int RecipeVersion);
