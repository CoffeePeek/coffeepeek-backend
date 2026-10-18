using CoffeePeek.Contract.Events.Photo;

namespace CoffeePeek.MediaService.Tests;

public static class PhotoVariantOutboxProbeHandler
{
    public static Task Handle(CheckInPhotoVariantsReadyEvent message, PhotoVariantOutboxProbe probe) => probe.ReceiveAsync(message);
}
