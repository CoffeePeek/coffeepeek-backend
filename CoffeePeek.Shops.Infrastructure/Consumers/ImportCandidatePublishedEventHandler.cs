using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Responses;
using CoffeePeek.Shops.Application.Services;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class ImportCandidatePublishedEventHandler(
    ICreateShopFromImportService createShopService,
    ILogger<ImportCandidatePublishedEventHandler> logger)
{
    public async Task<ImportCandidatePublishCompleteResponse> Handle(
        ImportCandidatePublishedEvent message,
        CancellationToken ct)
    {
        var results = new List<ImportCandidatePublishResult>(message.Items.Count);
        var createdInBatch = new Dictionary<Guid, Guid>();

        foreach (var item in message.Items)
        {
            // The final EF flush happens after this handler returns. A duplicate
            // candidate in the same batch is therefore not yet visible to a DB query.
            if (createdInBatch.TryGetValue(item.CandidateId, out var existingShopId))
            {
                results.Add(new ImportCandidatePublishResult(item.CandidateId, existingShopId));
                continue;
            }

            logger.LogInformation(
                "Creating shop from import candidate {CandidateId} ({ShopName})",
                item.CandidateId,
                item.Name);

            var shopId = await createShopService.CreateShopFromImportAsync(item, ct);
            createdInBatch.Add(item.CandidateId, shopId);
            results.Add(new ImportCandidatePublishResult(item.CandidateId, shopId));
        }

        return new ImportCandidatePublishCompleteResponse(results);
    }
}
