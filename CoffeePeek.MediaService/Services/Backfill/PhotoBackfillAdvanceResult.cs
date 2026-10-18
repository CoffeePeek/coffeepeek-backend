using CoffeePeek.MediaService.Data;

namespace CoffeePeek.MediaService.Services.Backfill;

public sealed record PhotoBackfillAdvanceResult(string Outcome, PhotoBackfillRun Run);
