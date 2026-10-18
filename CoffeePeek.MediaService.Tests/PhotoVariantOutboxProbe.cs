using System.Threading.Channels;
using CoffeePeek.Contract.Events.Photo;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoVariantOutboxProbe(Func<CheckInPhotoVariantsReadyEvent, Task<bool>> observe)
{
    public Channel<(Guid JobId, bool Committed)> Observations { get; } = Channel.CreateUnbounded<(Guid, bool)>();
    public async Task ReceiveAsync(CheckInPhotoVariantsReadyEvent message) =>
        await Observations.Writer.WriteAsync((message.JobId, await observe(message)));
}
