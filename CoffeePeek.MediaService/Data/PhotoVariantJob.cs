namespace CoffeePeek.MediaService.Data;

/// <summary>Durable work, committed with attachment confirmation. Rendering never holds this transaction open.</summary>
public sealed class PhotoVariantJob
{
    public Guid Id { get; set; }
    public Guid PhotoId { get; set; }
    public Guid CheckInId { get; set; }
    public Guid OwnerId { get; set; }
    public string SourceKey { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public string? SourceDigest { get; set; }
    public int RecipeVersion { get; set; }
    public PhotoVariantJobStatus Status { get; set; }
    public int Attempts { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    /// <summary>Zero is a new user upload; one is explicitly requested archive work.</summary>
    public int Priority { get; set; }
    public Guid? BackfillRunId { get; set; }
}
