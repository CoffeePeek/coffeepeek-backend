namespace CoffeePeek.MediaService.Services.Backfill;

public sealed record CreatePhotoBackfillRun(DateTime BeforeUtc, DateTime RecentSinceUtc,
    int BatchSize = 10, int MinBatchIntervalSeconds = 5);
