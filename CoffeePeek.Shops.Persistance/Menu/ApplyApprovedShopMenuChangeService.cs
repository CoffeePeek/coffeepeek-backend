using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Contract.Events;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainSource = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemSource;

namespace CoffeePeek.Shops.Persistance.Menu;

public sealed class ApplyApprovedShopMenuChangeService(ShopsDbContext db,
    IUploadedPhotoLookup photoLookup, IMessageBus bus)
    : IApplyApprovedShopMenuChangeService
{
    public async Task<ShopChangeRequestApplyFailedV1?> ApplyAsync(
        ShopChangeRequestApprovedV1 message, CancellationToken ct)
    {
        if (await db.AppliedShopChangeRequests.AnyAsync(x => x.Id == message.RequestId, ct))
            return null;

        try
        {
            var (newPhotoKeys, changed) = await ApplyMenuAsync(message, ct);
            db.AppliedShopChangeRequests.Add(AppliedShopChangeRequest.Create(
                message.RequestId, message.ShopId, message.ApprovedByUserId, DateTime.UtcNow));
            if (newPhotoKeys.Length > 0)
                await bus.PublishAsync(new ShopPhotosAttachedEvent(message.SubmittedByUserId,
                    message.ShopId, ShopPhotoAttachmentKind.Menu, newPhotoKeys));
            await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(message.ShopId));
            if (changed)
                await bus.PublishAsync(new CoffeePeek.Contract.Events.Leaderboards.ActivityScoreChanged(
                    CoffeePeek.Shared.Kernel.Leaderboards.LeaderboardActivityKind.ShopEdited,
                    message.RequestId, message.SubmittedByUserId, 1, true, DateTime.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException or ArgumentException
            or ValidationException)
        {
            db.ChangeTracker.Clear();
            return new ShopChangeRequestApplyFailedV1(message.RequestId, message.ShopId, ex.Message, DateTime.UtcNow);
        }

        return null;
    }

    private async Task<(string[] PhotoKeys, bool Changed)> ApplyMenuAsync(ShopChangeRequestApprovedV1 message, CancellationToken ct)
    {
        var change = message.Payload.Menu ?? throw new DomainException("Menu payload is required.");
        if (change.ExpectedInputRevision is null)
            throw new DomainException("Menu revision is required; legacy approval requires manual reconciliation.");
        if (change.ExpectedInputRevision < 0)
            throw new DomainException("Expected menu revision cannot be negative.");
        // Old queued events allowed null collections and treated them as empty.
        var rows = change.Items ?? [];
        var retainedPhotoIds = change.RetainedPhotoIds ?? [];
        var uploadedPhotos = change.NewPhotos ?? [];

        if (!await db.Shops.AnyAsync(x => x.Id == message.ShopId, ct))
            throw new NotFoundException($"Published shop '{message.ShopId}' was not found.");

        var menu = await db.ShopMenus
            .Include(x => x.Items)
            .Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.CoffeeShopId == message.ShopId, ct);
        var currentRevision = menu?.InputRevision ?? 0L;
        if (change.ExpectedInputRevision is { } expected && expected != currentRevision)
            throw new DomainException($"Menu changed after submission (expected revision {expected}, current {currentRevision}).");

        if (menu is null && retainedPhotoIds.Count > 0)
            throw new DomainException("Cannot retain photos because this shop has no menu.");
        if (rows.Select(x => x.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
            throw new DomainException("Menu item slugs cannot contain duplicates.");
        if (retainedPhotoIds.Distinct().Count() != retainedPhotoIds.Count)
            throw new DomainException("Retained menu photo IDs cannot contain duplicates.");

        var photosById = menu?.Photos.ToDictionary(x => x.Id) ?? new Dictionary<Guid, ShopMenuPhoto>();
        if (retainedPhotoIds.Any(id => !photosById.ContainsKey(id)))
            throw new DomainException("One or more retained menu photos do not belong to this shop.");

        var definitions = await db.CoffeeDrinkDefinitions.Where(x => x.IsActive).ToListAsync(ct);
        var bySlug = definitions.ToDictionary(x => x.Slug, StringComparer.OrdinalIgnoreCase);
        var items = new List<ShopMenuItem>();
        foreach (var row in rows)
        {
            if (!bySlug.TryGetValue(row.Slug, out var definition))
                throw new DomainException($"Coffee drink '{row.Slug}' was not found.");
            items.Add(ShopMenuItem.Create(
                definition.Id,
                (DomainAvailability)(int)row.Availability,
                row.Price,
                DomainSource.Manual));
        }

        var claimed = uploadedPhotos.Count == 0 ? [] : await TrustedUploadedShopPhotos.ClaimAsync(
            photoLookup, message.SubmittedByUserId, message.ShopId,
            ShopPhotoAttachmentKind.Menu, uploadedPhotos, ct);

        menu ??= ShopMenu.Create(message.ShopId);
        if (db.Entry(menu).State == EntityState.Detached)
            db.ShopMenus.Add(menu);
        var existingDrinkIds = menu.Items.Select(item => item.DrinkDefinitionId).ToHashSet();
        var changed = !menu.Items.OrderBy(x => x.DrinkDefinitionId).Select(x => (x.DrinkDefinitionId, x.Availability, x.Price))
            .SequenceEqual(items.OrderBy(x => x.DrinkDefinitionId).Select(x => (x.DrinkDefinitionId, x.Availability, x.Price)))
            || claimed.Count > 0 || !menu.Photos.Select(x => x.Id).ToHashSet().SetEquals(retainedPhotoIds);
        menu.ApplyApprovedManualSnapshot(items, message.ApprovedAtUtc, message.ApprovedByUserId);
        var newPhotos = claimed.Select(x =>
            ShopMenuPhoto.Create(x.FileName, x.ContentType, x.StorageKey, x.SizeBytes, x.PhotoId)).ToArray();
        menu.ReplacePhotos(retainedPhotoIds.Select(id => photosById[id]).Concat(newPhotos));

        // These children have application-assigned GUIDs. When added to a previously tracked menu,
        // EF otherwise treats them as existing rows and issues UPDATE instead of INSERT.
        foreach (var item in items.Where(item => !existingDrinkIds.Contains(item.DrinkDefinitionId)))
            db.Entry(item).State = EntityState.Added;
        foreach (var photo in newPhotos)
            db.Entry(photo).State = EntityState.Added;
        return (claimed.Select(p => p.StorageKey).ToArray(), changed);
    }

}
