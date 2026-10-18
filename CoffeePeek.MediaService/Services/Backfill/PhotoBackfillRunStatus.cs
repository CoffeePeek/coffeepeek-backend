using CoffeePeek.MediaService.Data;

namespace CoffeePeek.MediaService.Services.Backfill;

public sealed record PhotoBackfillRunStatus(PhotoBackfillRun Run, Dictionary<string, int> Jobs,
    bool GenerationDrained);
