namespace CoffeePeek.MediaService.Services;

/// <summary>Download-only storage port; no bucket provisioning or object writes.</summary>
public interface ICheckInPhotoReader
{
    Task CopyToAsync(string key, Stream destination, CancellationToken ct);
}
