using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public interface ICheckInPhotoBackfillCandidates
{
    Task<PhotoBackfillPage> ReadAsync(PhotoBackfillPageRequest request, CancellationToken ct);
}
