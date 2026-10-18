#nullable enable
namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record PhotoBackfillPage(PhotoBackfillCandidate[] Items, PhotoBackfillCursor? NextCursor);
