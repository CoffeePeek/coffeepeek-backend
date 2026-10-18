using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CheckIns;

/// <summary>Internal Community binding observation; it grants no authority to read or mutate storage.</summary>
public sealed record PhotoBackfillCandidate(Guid PhotoId, Guid CheckInId, Guid OwnerId, string StorageKey,
    string SourceVersion, CheckInKind Kind, bool IsDrink, DateTime SortAtUtc, long DeclaredSizeBytes, string ContentType);
