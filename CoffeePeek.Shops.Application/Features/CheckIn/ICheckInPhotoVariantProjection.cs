using CoffeePeek.Contract.Events.Photo;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public interface ICheckInPhotoVariantProjection
{
    Task ApplyAsync(CheckInPhotoVariantsReadyEvent message, CancellationToken ct);
}
