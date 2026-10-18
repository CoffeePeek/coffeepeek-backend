using CoffeePeek.Contract.Enums;

namespace CoffeePeek.MediaService.Data;

/// <summary>A durable scan checkpoint, independent from the lifetime of queued rendering jobs.</summary>
public sealed class PhotoBackfillRun
{
    public Guid Id { get; set; }
    public DateTime BeforeUtc { get; set; }
    public DateTime RecentSinceUtc { get; set; }
    public int RecipeVersion { get; set; }
    public PhotoBackfillPhase Phase { get; set; }
    public DateTime? CursorSortAtUtc { get; set; }
    public Guid? CursorCheckInId { get; set; }
    public Guid? CursorPhotoId { get; set; }
    public int BatchSize { get; set; }
    public int MinBatchIntervalSeconds { get; set; }
    public bool Paused { get; set; } = true;
    public bool ScanComplete { get; set; }
    public DateTime NextBatchAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long ScannedCount { get; set; }
    public long EnqueuedCount { get; set; }
    public long ExistingCount { get; set; }
    public long BlockedCount { get; set; }
}
