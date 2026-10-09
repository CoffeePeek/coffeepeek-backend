using System.Text.Json;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;
using CoffeePeek.Shops.Application.Observability;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Menu;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine.Attributes;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

/// <summary>OCR runs outside the EF transaction; each job transition uses its own short scope.</summary>
[NonTransactional]
public sealed class ParseMenuOcrJobRequestedEventHandler(
    IServiceScopeFactory scopeFactory,
    IOptions<MediaPublicUrlOptions> mediaOptions,
    IMenuVisionParser parser,
    IQueryCoffeeDrinkRepository drinks,
    IMenuPhotoDownloader downloader,
    IOptions<MenuPriceRangeOptions> priceOptions,
    ILogger<ParseMenuOcrJobRequestedEventHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task Handle(ParseMenuOcrJobRequestedEvent message, CancellationToken ct)
    {
        using var activity = ShopsTelemetry.ActivitySource.StartActivity("shops.ocr.process");
        activity?.SetTag("ocr.job_id", message.JobId);
        var job = await WithStoreAsync(store => store.GetAsync(message.JobId, ct));
        if (job is null)
        {
            logger.LogWarning("OCR job {JobId} was not found", message.JobId);
            return;
        }

        if (job.Status == MenuOcrJobStatus.Pending)
        {
            var claimed = await WithStoreAsync(store => store.TryStartCurrentAsync(message.JobId, ct));
            if (!claimed)
                return;

            ParseMenuPhotosResponse data;
            try
            {
                var photos = JsonSerializer.Deserialize<MenuOcrPhotoSnapshot[]>(job.InputPhotosJson, JsonOptions)
                             ?? throw new InvalidOperationException($"OCR job {message.JobId} has invalid photos.");
                var media = mediaOptions.Value;
                var inputs = photos.Select(p => new ParseMenuPhotoInput(
                    p.StorageKey,
                    p.ContentType,
                    MediaStorageUrlBuilder.BuildPublicUrl(media.PublicEndpoint, media.ShopBucketName, p.StorageKey)))
                    .ToArray();
                var parsed = await ParseMenuPhotosHandler.Handle(
                    new ParseMenuPhotosCommand(inputs), parser, drinks, downloader, priceOptions, logger, ct);
                data = parsed.Data ?? new ParseMenuPhotosResponse(false, "Parse failed.", null, [], []);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "OCR job {JobId} failed before a result could be stored", message.JobId);
                data = new ParseMenuPhotosResponse(false, "Menu OCR failed.", null, [], []);
            }

            // Persist the provider result independently before any menu mutation. A retry after
            // this commit skips the paid call and applies the same saved response.
            var resultJson = JsonSerializer.Serialize(data, JsonOptions);
            var stored = await WithStoreAsync(store => store.StoreResultAsync(message.JobId, resultJson, ct));
            if (!stored)
                return;
        }
        else if (job.Status != MenuOcrJobStatus.ResultStored)
        {
            // Running means another attempt may be in flight or its provider outcome is unknown.
            // Automatic redelivery must not make another paid call.
            return;
        }

        await WithStoreAsync(store => store.ApplyStoredAsync(message.JobId, message.RequestedByUserId, ct));
    }

    private async Task<T> WithStoreAsync<T>(Func<MenuOcrJobStore, Task<T>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>());
    }

    private async Task WithStoreAsync(Func<MenuOcrJobStore, Task> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>());
    }
}
