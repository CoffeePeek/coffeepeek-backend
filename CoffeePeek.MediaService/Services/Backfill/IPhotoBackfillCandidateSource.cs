using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.MediaService.Services.Backfill;

public interface IPhotoBackfillCandidateSource
{
    Task<PhotoBackfillPage> ReadAsync(PhotoBackfillPageRequest request, CancellationToken ct);
}
