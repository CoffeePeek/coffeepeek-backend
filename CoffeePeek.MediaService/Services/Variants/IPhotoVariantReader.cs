namespace CoffeePeek.MediaService.Services.Variants;

public interface IPhotoVariantReader
{
    Task AuditPrivacyAsync(CancellationToken ct);
    Task CopyToAsync(string key, Stream destination, CancellationToken ct);
}
