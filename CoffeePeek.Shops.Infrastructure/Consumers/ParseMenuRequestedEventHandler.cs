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
    IApplyShopMenuService applyMenu,
    IQueryCoffeeShopRepository shopRepository,
    IUnitOfWork unitOfWork,
    IOptions<MediaPublicUrlOptions> mediaOptions,
    IMenuVisionParser parser,
    IQueryCoffeeDrinkRepository drinks,
    IMenuPhotoDownloader downloader,
    IOptions<MenuPriceRangeOptions> priceOptions,
    ILogger<ParseMenuRequestedEventHandler> logger)
{
    private const int MaxApplyAndSaveAttempts = 3;

    public async Task<MenuParsedEvent> Handle(ParseMenuRequestedEvent message, CancellationToken ct)
    {
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
            var shopId = message.PublishedShopId
                         ?? await shopRepository.GetIdByModerationId(message.SourceId, ct);

            // Correlated moderation results are applied only after Moderation accepts their revision.
            if (shopId.HasValue && message.ParseRequestId is null)
            {
                var photoSnapshots = message.Photos.Select(p => new ShopMenuPhotoSnapshot(
                    p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.MediaPhotoId)).ToArray();

                for (var attempt = 1; attempt <= MaxApplyAndSaveAttempts; attempt++)
                {
                    try
                    {
                        if (data.Success)
                        {
                            await applyMenu.ApplyParseResultAsync(
                                shopId.Value,
                                data.Items,
                                data.Unmatched,
                                data.SuggestedPriceRange,
                                photoSnapshots,
                                capturedAt,
                                message.RequestedByUserId,
                                ct);
                        }
                        else
                        {
                            await applyMenu.MarkParseFailedAsync(shopId.Value, data.Error ?? "Parse failed.", ct);
                        }

                        await unitOfWork.SaveChangesAsync(ct);
                        break;
                    }
                    catch (ConflictException ex) when (ex.InnerException is DbUpdateConcurrencyException &&
                                                        attempt < MaxApplyAndSaveAttempts)
                    {
                        logger.LogWarning(
                            ex,
                            "Concurrency conflict applying menu parse result for shop {ShopId}, attempt {Attempt}/{MaxAttempts}",
                            shopId,
                            attempt,
                            MaxApplyAndSaveAttempts);
                        unitOfWork.ClearTracking();
                    }
                    catch (ConflictException ex) when (ex.InnerException is DbUpdateConcurrencyException)
                    {
                        // Out of local retries. Rethrowing the conflict would let the Wolverine retry policy re-run the
                        // whole handler, i.e. download the photos and bill another vision call per attempt.
                        logger.LogError(ex, "Giving up applying menu parse result for shop {ShopId}", shopId);
                        throw new InvalidOperationException(
                            $"Menu parse result for shop {shopId} could not be saved after {MaxApplyAndSaveAttempts} attempts.");
                    }
                }
            }

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
