namespace CoffeePeek.MediaService.Data;

/// <summary>Check-in deletion is irreversible; survives delete-before-attachment message ordering.</summary>
public sealed class CheckInPhotoTombstone
{
    public Guid CheckInId { get; set; }
    public DateTime DeletedAtUtc { get; set; }
}
