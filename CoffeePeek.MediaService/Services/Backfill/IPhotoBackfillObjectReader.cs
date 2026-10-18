namespace CoffeePeek.MediaService.Services.Backfill;

public interface IPhotoBackfillObjectReader
{
    Task<PhotoBackfillObject> OpenAsync(string key, CancellationToken ct);
}
