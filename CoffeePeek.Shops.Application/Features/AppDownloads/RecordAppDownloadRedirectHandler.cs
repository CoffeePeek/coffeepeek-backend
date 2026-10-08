using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class RecordAppDownloadRedirectHandler
{
    public static async Task Handle(
        RecordAppDownloadRedirectCommand command,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (await repository.HasRedirectEventAsync(command.EventId, ct))
            return;

        repository.AddRedirectEvent(AppDownloadRedirectEvent.Create(
            command.EventId,
            command.Channel,
            command.UserAgent,
            command.Referer,
            command.Country));
        await unitOfWork.SaveChangesAsync(ct);
    }
}
