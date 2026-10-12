using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class ParseMenuRequestedEventHandler(
    IOptions<MediaPublicUrlOptions> mediaOptions,
    IMenuVisionParser parser,
    IQueryCoffeeDrinkRepository drinks,
    IMenuPhotoDownloader downloader,
    IOptions<MenuPriceRangeOptions> priceOptions,
    ILogger<ParseMenuRequestedEventHandler> logger)
{
    public async Task<object> Handle(ParseMenuRequestedEvent message, CancellationToken ct)
    {
        // P15 briefly emitted this legacy envelope with JobId. Forward it to the new
        // short-transaction worker without paying for OCR in this eager EF transaction.
        if (message.SourceKind == MenuParseSourceKind.PublishedShop && message.JobId is { } jobId)
            return new ParseMenuOcrJobRequestedEvent(jobId, message.RequestedByUserId);

        if (message.SourceKind == MenuParseSourceKind.PublishedShop)
        {
            var publishedShopId = message.PublishedShopId ?? message.SourceId;
            logger.LogWarning("Quarantined legacy published menu OCR for shop {ShopId} without JobId", publishedShopId);
            return new MenuParsedEvent(
                message.SourceKind, message.SourceId, publishedShopId, false,
                "Legacy published OCR requires an explicit migration to a persisted job.",
                null, [], [], DateTime.UtcNow);
        }

        logger.LogInformation(
            "Starting menu parse for {Kind} {SourceId} photos={PhotoCount} publishedShop={PublishedShopId}",
            message.SourceKind,
            message.SourceId,
            message.Photos.Count,
            message.PublishedShopId);

        try
        {
            var media = mediaOptions.Value;
            var inputs = message.Photos
                .Select(p => new ParseMenuPhotoInput(
                    p.StorageKey,
                    p.ContentType,
                    MediaStorageUrlBuilder.BuildPublicUrl(media.PublicEndpoint, media.ShopBucketName, p.StorageKey)))
                .ToArray();

            var parsed = await ParseMenuPhotosHandler.Handle(
                new ParseMenuPhotosCommand(inputs),
                parser,
                drinks,
                downloader,
                priceOptions,
                logger,
                ct);

            var data = parsed.Data ?? new ParseMenuPhotosResponse(false, "Parse failed.", null, [], []);
            var capturedAt = DateTime.UtcNow;
            // Legacy draft OCR can update only its draft; published menus require a persisted job or revision.
            var shopId = message.PublishedShopId;

            if (data.Success)
            {
                logger.LogInformation(
                    "Menu parse succeeded for {Kind} {SourceId} shop={ShopId} items={ItemCount} unmatched={UnmatchedCount}",
                    message.SourceKind,
                    message.SourceId,
                    shopId,
                    data.Items.Count,
                    data.Unmatched.Count);
            }
            else
            {
                logger.LogError(
                    "Menu parse failed for {Kind} {SourceId} shop={ShopId}: {Error}",
                    message.SourceKind,
                    message.SourceId,
                    shopId,
                    data.Error ?? "Parse failed.");
            }

            return new MenuParsedEvent(
                message.SourceKind,
                message.SourceId,
                shopId,
                data.Success,
                data.Error,
                data.SuggestedPriceRange,
                data.Items,
                data.Unmatched,
                capturedAt,
                message.ParseRequestId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Menu parse threw for {Kind} {SourceId}",
                message.SourceKind,
                message.SourceId);
            throw;
        }
    }
}
