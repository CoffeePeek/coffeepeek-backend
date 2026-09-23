using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainSource = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemSource;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class ShopChangeRequestApprovedHandler(
    ShopsDbContext dbContext,
    ICacheService cacheService,
    ILogger<ShopChangeRequestApprovedHandler> logger)
{
    public async Task Handle(ShopChangeRequestApprovedV1 message, CancellationToken ct)
    {
        if (await dbContext.AppliedShopChangeRequests.AnyAsync(x => x.Id == message.RequestId, ct))
        {
            logger.LogInformation("Shop change request {RequestId} was already applied", message.RequestId);
            return;
        }

        var shop = await dbContext.Shops
            .AsSplitQuery()
            .Include(x => x.ShopPhotos)
            .Include(x => x.ShopTags)
            .Include(x => x.Equipments)
            .Include(x => x.Roasters)
            .Include(x => x.BrewMethods)
            .FirstOrDefaultAsync(x => x.Id == message.ShopId, ct)
            ?? throw new NotFoundException($"Published shop '{message.ShopId}' was not found.");

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
                ApplyGallery(shop, message.Payload.Photos, message.SubmittedByUserId);
                break;
            case CoffeePeek.Contract.Enums.ShopChangeSection.Menu:
                await ApplyMenu(message, ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(message.Section));
        }

        dbContext.AppliedShopChangeRequests.Add(AppliedShopChangeRequest.Create(
            message.RequestId, message.ShopId, message.ApprovedByUserId, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(ct);

        await cacheService.RemoveAsync(CacheKey.Shop.Detail(message.ShopId));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
        logger.LogInformation(
            "Applied {Section} from shop change request {RequestId} to shop {ShopId}",
            message.Section, message.RequestId, message.ShopId);
    }

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

    private void ApplyGallery(
        CoffeeShop shop,
        CoffeePeek.Contract.Dtos.CoffeeShop.ShopChangeGalleryDto? gallery,
        Guid submittedByUserId)
    {
        if (gallery is null) throw new DomainException("Photos payload is required.");
        var retained = gallery.RetainedPhotoIds ?? [];
        var currentIds = shop.ShopPhotos.Select(x => x.Id).ToHashSet();
        if (retained.Any(id => !currentIds.Contains(id)))
            throw new DomainException("One or more retained gallery photos do not belong to this shop.");

        var removed = shop.ShopPhotos.Where(x => !retained.Contains(x.Id)).ToList();
        dbContext.ShopPhotos.RemoveRange(removed);
        var result = shop.RemovePhotos(removed.Select(x => x.Id).ToList());
        if (result.IsFailed) throw new DomainException(result.Errors[0].Message);

        var added = (gallery.NewPhotos ?? [])
            .Select(x => new ShopPhoto(x.FileName, x.ContentType, x.StorageKey, x.Size, submittedByUserId))
            .ToList();
        shop.AddPhotos(added);
        dbContext.ShopPhotos.AddRange(added);
        var reordered = shop.ReorderPhotos(retained.Concat(added.Select(x => x.Id)).ToList());
        if (reordered.IsFailed) throw new DomainException(reordered.Errors[0].Message);
    }

    private async Task ApplyMenu(ShopChangeRequestApprovedV1 message, CancellationToken ct)
    {
        var change = message.Payload.Menu ?? throw new DomainException("Menu payload is required.");
        var definitions = await dbContext.CoffeeDrinkDefinitions
            .Where(x => x.IsActive)
            .ToListAsync(ct);
        var bySlug = definitions.ToDictionary(x => x.Slug, StringComparer.OrdinalIgnoreCase);

        var items = new List<ShopMenuItem>();
        foreach (var row in change.Items ?? [])
        {
            if (!bySlug.TryGetValue(row.Slug, out var definition))
                throw new DomainException($"Coffee drink '{row.Slug}' was not found.");
            items.Add(ShopMenuItem.Create(
                definition.Id,
                (DomainAvailability)(int)row.Availability,
                row.Price,
                row.VolumeMl,
                DomainSource.Manual));
        }

        var menu = await dbContext.ShopMenus
            .Include(x => x.Items)
            .Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.CoffeeShopId == message.ShopId, ct);
        if (menu is null)
        {
            if ((change.RetainedPhotoIds?.Count ?? 0) > 0)
                throw new DomainException("Cannot retain photos because this shop has no menu.");
            menu = ShopMenu.Create(message.ShopId);
            dbContext.ShopMenus.Add(menu);
        }

        var retainedIds = change.RetainedPhotoIds ?? [];
        var photosById = menu.Photos.ToDictionary(x => x.Id);
        if (retainedIds.Any(id => !photosById.ContainsKey(id)))
            throw new DomainException("One or more retained menu photos do not belong to this shop.");

        menu.MarkManuallyCaptured(message.ApprovedAtUtc);
        menu.ApplyParsedItems(items, menu.UnmatchedJson, menu.SuggestedPriceRange, message.ApprovedByUserId);
        var photos = retainedIds.Select(id => photosById[id]).Concat(
            (change.NewPhotos ?? []).Select(x => ShopMenuPhoto.Create(
                x.FileName, x.ContentType, x.StorageKey, x.Size)));
        menu.ReplacePhotos(photos);
    }
}
