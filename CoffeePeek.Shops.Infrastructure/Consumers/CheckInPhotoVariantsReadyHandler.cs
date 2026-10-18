using CoffeePeek.Contract.Events.Photo;
using CoffeePeek.Shops.Application.Features.CheckIn;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public static class CheckInPhotoVariantsReadyHandler
{
    public static Task Handle(CheckInPhotoVariantsReadyEvent message, ICheckInPhotoVariantProjection projection,
        CancellationToken ct) => projection.ApplyAsync(message, ct);
}
