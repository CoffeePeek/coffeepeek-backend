namespace CoffeePeek.MediaService.Data;

/// <summary>Write intent persisted before PUT, including attempts that crash before Ready.</summary>
public sealed class PhotoVariantArtifact
{
    public string ObjectKey { get; set; } = "";
    public Guid JobId { get; set; }
    public Guid LeaseToken { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
