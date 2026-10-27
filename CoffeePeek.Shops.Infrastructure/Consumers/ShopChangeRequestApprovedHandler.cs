using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Contract.Events;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class ShopChangeRequestApprovedHandler(
    ShopsDbContext dbContext,
    ILogger<ShopChangeRequestApprovedHandler> logger,
    IApplyApprovedShopMenuChangeService menuChanges,
    IUploadedPhotoLookup photoLookup,
    IMessageBus bus)
{
    public async Task<ShopChangeRequestApplyFailedV1?> Handle(ShopChangeRequestApprovedV1 message, CancellationToken ct)
    {
        if (message.Section == CoffeePeek.Contract.Enums.ShopChangeSection.Menu)
        {
            var failure = await menuChanges.ApplyAsync(message, ct);
            if (failure is null)
                logger.LogInformation("Applied or replayed Menu change request {RequestId} for shop {ShopId}",
                    message.RequestId, message.ShopId);
            else
                logger.LogWarning("Menu change request {RequestId} for shop {ShopId} was rejected: {Reason}",
                    message.RequestId, message.ShopId, failure.Reason);
            return failure;
        }

        if (await dbContext.AppliedShopChangeRequests.AnyAsync(x => x.Id == message.RequestId, ct))
        {
            logger.LogInformation("Shop change request {RequestId} was already applied", message.RequestId);
            return null;
        }

        // State may have drifted since submission (shop deleted, tag deactivated, photo removed). Those failures are
        // permanent: dead-lettering left the request Approved in Moderation forever, so report them back instead.
        var changed = false;
        try
        {
            var shop = await dbContext.Shops
                .AsSplitQuery()
                .Include(x => x.ShopPhotos)
                .Include(x => x.ShopTags)
                .Include(x => x.ShopEquipments).ThenInclude(x => x.Equipment)
                .Include(x => x.Roasters)
                .Include(x => x.BrewMethods)
                .FirstOrDefaultAsync(x => x.Id == message.ShopId, ct)
                ?? throw new NotFoundException($"Published shop '{message.ShopId}' was not found.");

            var before = ProfileContent(shop);
            switch (message.Section)
            {
                case CoffeePeek.Contract.Enums.ShopChangeSection.Description:
                    shop.UpdateDetails(shop.Name, message.Payload.Description, shop.PriceRange);
                    break;
                case CoffeePeek.Contract.Enums.ShopChangeSection.Contacts:
                    ApplyContacts(shop, message.Payload.Contacts);
                    break;
                case CoffeePeek.Contract.Enums.ShopChangeSection.Tags:
                    await ApplyTags(shop, message.Payload.TagIds, message.ApprovedByUserId, ct);
                    break;
                case CoffeePeek.Contract.Enums.ShopChangeSection.Roasters:
                    shop.SetRoasters(await LoadCatalog(
                        dbContext.Roasters, message.Payload.RoasterIds, "roasters", ct));
                    break;
                case CoffeePeek.Contract.Enums.ShopChangeSection.Equipment:
                    shop.SetEquipment(await LoadCatalog(
                        dbContext.Equipments, message.Payload.EquipmentIds, "equipment", ct));
                    break;
                case CoffeePeek.Contract.Enums.ShopChangeSection.BrewMethods:
                    shop.SetBrewMethods(await LoadCatalog(
                        dbContext.BrewMethods, message.Payload.BrewMethodIds, "brew methods", ct));
                    break;
                case CoffeePeek.Contract.Enums.ShopChangeSection.Photos:
                    var newPhotoKeys = await ApplyGalleryAsync(shop, message.Payload.Photos,
                        message.SubmittedByUserId, ct);
                    if (newPhotoKeys.Length > 0)
                        await bus.PublishAsync(new ShopPhotosAttachedEvent(message.SubmittedByUserId,
                            shop.Id, ShopPhotoAttachmentKind.Gallery, newPhotoKeys));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(message.Section));
            }

            // Moderation writes the same published profile as admin and owner PUTs.
            // Advancing its concurrency token makes an older If-Match unusable.
            changed = before != ProfileContent(shop);
            shop.AdvanceProfileRevision();
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException or ArgumentException
            or ValidationException)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(ex,
                "Shop change request {RequestId} for shop {ShopId} could not be applied", message.RequestId, message.ShopId);
            return new ShopChangeRequestApplyFailedV1(message.RequestId, message.ShopId, ex.Message, DateTime.UtcNow);
        }

        dbContext.AppliedShopChangeRequests.Add(AppliedShopChangeRequest.Create(
            message.RequestId, message.ShopId, message.ApprovedByUserId, DateTime.UtcNow));
        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(message.ShopId));
        if (changed)
            await bus.PublishAsync(new CoffeePeek.Contract.Events.Leaderboards.ActivityScoreChanged(
                CoffeePeek.Shared.Kernel.Leaderboards.LeaderboardActivityKind.ShopEdited,
                message.RequestId, message.SubmittedByUserId, 1, true, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation(
            "Applied {Section} from shop change request {RequestId} to shop {ShopId}",
            message.Section, message.RequestId, message.ShopId);
        return null;
    }

    private static string ProfileContent(CoffeeShop shop) => System.Text.Json.JsonSerializer.Serialize(new
    {
        shop.Description,
        Contact = new { shop.Contact?.InstagramLink, shop.Contact?.Email, shop.Contact?.SiteLink, shop.Contact?.PhoneNumber },
        Tags = shop.ShopTags.Select(x => x.TagId).Order().ToArray(),
        Roasters = shop.Roasters.Select(x => x.Id).Order().ToArray(),
        Equipment = shop.ShopEquipments.OrderBy(x => x.EquipmentId).Select(x => new { x.EquipmentId, x.IsPrimary }).ToArray(),
        Methods = shop.BrewMethods.Select(x => x.Id).Order().ToArray(),
        Photos = shop.ShopPhotos.OrderBy(x => x.SortIndex).Select(x => x.Id).ToArray()
    });

    private static void ApplyContacts(
        CoffeeShop shop,
        CoffeePeek.Contract.Dtos.CoffeeShop.ShopChangeContactsDto? contacts)
    {
        if (contacts is null) throw new DomainException("Contacts payload is required.");
        shop.SetContact(contacts.InstagramLink, contacts.Email, contacts.SiteLink, contacts.PhoneNumber);
    }

    private async Task ApplyTags(
        CoffeeShop shop,
        IReadOnlyList<Guid>? ids,
        Guid approvedByUserId,
        CancellationToken ct)
    {
        ids ??= [];
        var distinct = ids.Distinct().ToList();
        var existingCount = await dbContext.ShopTags.CountAsync(x => distinct.Contains(x.Id) && x.IsActive, ct);
        if (existingCount != distinct.Count) throw new DomainException("One or more shop tag IDs were not found.");
        shop.SetTags(distinct, approvedByUserId);
    }

    private static async Task<List<T>> LoadCatalog<T>(
        DbSet<T> set,
        IReadOnlyList<Guid>? ids,
        string name,
        CancellationToken ct) where T : CoffeePeek.Shared.Domain.Entities.Entity<Guid>
    {
        ids ??= [];
        var distinct = ids.Distinct().ToList();
        var items = await set.Where(x => distinct.Contains(x.Id)).ToListAsync(ct);
        if (items.Count != distinct.Count) throw new DomainException($"One or more {name} IDs were not found.");
        return items;
    }

    private async Task<string[]> ApplyGalleryAsync(
        CoffeeShop shop,
        CoffeePeek.Contract.Dtos.CoffeeShop.ShopChangeGalleryDto? gallery,
        Guid submittedByUserId,
        CancellationToken ct)
    {
        if (gallery is null) throw new DomainException("Photos payload is required.");
        var retained = gallery.RetainedPhotoIds ?? [];
        var currentIds = shop.ShopPhotos.Select(x => x.Id).ToHashSet();
        if (retained.Any(id => !currentIds.Contains(id)))
            throw new DomainException("One or more retained gallery photos do not belong to this shop.");

        var newPhotos = gallery.NewPhotos ?? [];
        var claimed = newPhotos.Count == 0 ? [] : await TrustedUploadedShopPhotos.ClaimAsync(
            photoLookup, submittedByUserId, shop.Id, ShopPhotoAttachmentKind.Gallery, newPhotos, ct);

        var removed = shop.ShopPhotos.Where(x => !retained.Contains(x.Id)).ToList();
        dbContext.ShopPhotos.RemoveRange(removed);
        var result = shop.RemovePhotos(removed.Select(x => x.Id).ToList());
        if (result.IsFailed) throw new DomainException(result.Errors[0].Message);

        var added = claimed
            .Select(x => new ShopPhoto(x.FileName, x.ContentType, x.StorageKey, x.SizeBytes,
                submittedByUserId, id: x.PhotoId))
            .ToList();
        shop.AddPhotos(added);
        dbContext.ShopPhotos.AddRange(added);
        var reordered = shop.ReorderPhotos(retained.Concat(added.Select(x => x.Id)).ToList());
        if (reordered.IsFailed) throw new DomainException(reordered.Errors[0].Message);
        return claimed.Select(p => p.StorageKey).ToArray();
    }

}
