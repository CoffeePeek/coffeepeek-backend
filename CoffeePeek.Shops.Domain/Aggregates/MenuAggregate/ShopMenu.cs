using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentResults;

namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed class ShopMenu : Entity<Guid>
{
    public Guid CoffeeShopId { get; private set; }
    public DateTime? CapturedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public string Currency { get; private set; } = BusinessConstants.DefaultMenuCurrency;
    public MenuParseStatus ParseStatus { get; private set; } = MenuParseStatus.None;
    public string? ParseError { get; private set; }
    public PriceRange? SuggestedPriceRange { get; private set; }
    public string? UnmatchedJson { get; private set; }
    public long InputRevision { get; private set; }
    public Guid? ActiveOcrJobId { get; private set; }

    private readonly List<ShopMenuItem> _items = [];
    public IReadOnlyCollection<ShopMenuItem> Items => _items.AsReadOnly();

    private readonly List<ShopMenuPhoto> _photos = [];
    public IReadOnlyCollection<ShopMenuPhoto> Photos => _photos.AsReadOnly();

    private readonly List<MenuOcrJob> _ocrJobs = [];
    public IReadOnlyCollection<MenuOcrJob> OcrJobs => _ocrJobs.AsReadOnly();

    // ReSharper disable once UnusedMember.Local
    private ShopMenu()
    {
    }

    public static ShopMenu Create(Guid coffeeShopId)
    {
        return new ShopMenu
        {
            Id = Guid.NewGuid(),
            CoffeeShopId = coffeeShopId,
            Currency = BusinessConstants.DefaultMenuCurrency,
            ParseStatus = MenuParseStatus.None
        };
    }

    public void MarkParsePending(DateTime capturedAtUtc)
    {
        ParseStatus = MenuParseStatus.Pending;
        ParseError = null;
        CapturedAtUtc ??= capturedAtUtc;
    }

    public void MarkManuallyCaptured(DateTime capturedAtUtc)
    {
        CapturedAtUtc = capturedAtUtc;
        InputRevision++;
    }

    public void MarkParseRunning()
    {
        ParseStatus = MenuParseStatus.Running;
        ParseError = null;
    }

    public void MarkParseFailed(string error)
    {
        ParseStatus = MenuParseStatus.Failed;
        ParseError = error.Length > BusinessConstants.MaxMenuParseErrorLength
            ? error[..BusinessConstants.MaxMenuParseErrorLength]
            : error;
    }

    public void ApplyParsedItems(
        IEnumerable<ShopMenuItem> items,
        string? unmatchedJson,
        PriceRange? suggestedPriceRange,
        Guid? updatedByUserId)
    {
        var prepared = items.ToArray();
        if (prepared.Any(i => i.Source == MenuItemSource.Manual))
            InputRevision++;
        _items.Clear();
        // One row per drink (unique index); a snapshot listing a drink twice used to fail the save with a 500.
        _items.AddRange(prepared.GroupBy(i => i.DrinkDefinitionId).Select(g => g.Last()));
        UnmatchedJson = unmatchedJson;
        SuggestedPriceRange = suggestedPriceRange;
        UpdatedByUserId = updatedByUserId;
        ParseStatus = MenuParseStatus.Ready;
        ParseError = null;
    }

    public void ApplyApprovedManualSnapshot(
        IEnumerable<ShopMenuItem> items,
        DateTime capturedAtUtc,
        Guid updatedByUserId)
    {
        var prepared = items.ToArray();
        if (prepared.Any(item => item.Source != MenuItemSource.Manual))
            throw new DomainException("Approved menu snapshot must contain manual items only.");
        if (prepared.Select(item => item.DrinkDefinitionId).Distinct().Count() != prepared.Length)
            throw new DomainException("Approved menu snapshot contains a repeated coffee drink.");
        var byDrink = prepared.ToDictionary(item => item.DrinkDefinitionId);

        // Preserve the persisted row for a drink already present. Replacing it with another GUID
        // in one SaveChanges can violate the unique (ShopMenuId, DrinkDefinitionId) constraint.
        _items.RemoveAll(item => !byDrink.ContainsKey(item.DrinkDefinitionId));
        foreach (var candidate in prepared)
        {
            var existing = _items.FirstOrDefault(item => item.DrinkDefinitionId == candidate.DrinkDefinitionId);
            if (existing is null)
                _items.Add(candidate);
            else
                existing.ApplyManual(candidate.Availability, candidate.Price, candidate.VolumeMl);
        }

        CapturedAtUtc = capturedAtUtc;
        UpdatedByUserId = updatedByUserId;
        InputRevision++;
        ParseStatus = MenuParseStatus.Ready;
        ParseError = null;
    }

    /// <summary>
    /// Applies a vision parse on top of the existing menu. A parse only covers the photos it was given, and
    /// manual edits outrank the parser, so items are upserted per drink instead of replacing the whole menu
    /// (which wiped earlier photos' items and every manual price on each re-parse).
    /// </summary>
    public void MergeParsedItems(
        IEnumerable<ShopMenuItem> parsed,
        string? unmatchedJson,
        PriceRange? suggestedPriceRange,
        Guid? updatedByUserId)
    {
        foreach (var item in parsed.GroupBy(i => i.DrinkDefinitionId).Select(g => g.Last()))
        {
            var existing = _items.FirstOrDefault(i => i.DrinkDefinitionId == item.DrinkDefinitionId);
            if (existing is null)
                _items.Add(item);
            else if (existing.Source != MenuItemSource.Manual)
                existing.ApplyParsed(item.Price, item.VolumeMl);
        }

        CompleteParse(unmatchedJson, suggestedPriceRange, updatedByUserId);
    }

    /// <summary>Finishes a parse that produced no catalog matches, so the menu does not stay Pending forever.</summary>
    public void CompleteParse(string? unmatchedJson, PriceRange? suggestedPriceRange, Guid? updatedByUserId)
    {
        UnmatchedJson = unmatchedJson;
        SuggestedPriceRange = suggestedPriceRange ?? SuggestedPriceRange;
        UpdatedByUserId = updatedByUserId;
        ParseStatus = MenuParseStatus.Ready;
        ParseError = null;
    }

    public void ReplacePhotos(IEnumerable<ShopMenuPhoto> photos)
    {
        var replacement = photos.ToArray();
        if (MenuOcrPhotoSet.Hash(_photos) != MenuOcrPhotoSet.Hash(replacement))
            InputRevision++;
        _photos.Clear();
        _photos.AddRange(replacement);
    }

    public void AddPhotos(IEnumerable<ShopMenuPhoto> photos)
    {
        var changed = false;
        foreach (var photo in photos)
        {
            if (_photos.Any(p => p.StorageKey == photo.StorageKey))
                continue;
            _photos.Add(photo);
            changed = true;
        }
        if (changed)
            InputRevision++;
    }

    public Result RemovePhotos(IReadOnlyList<Guid> photoIds)
    {
        if (photoIds is null || photoIds.Count == 0)
            return Result.Ok();

        if (photoIds.Distinct().Count() != photoIds.Count)
            return Result.Fail("Photo list contains duplicate photo IDs.");

        var byId = _photos.ToDictionary(photo => photo.Id);
        foreach (var photoId in photoIds)
        {
            if (!byId.ContainsKey(photoId))
                return Result.Fail($"Photo '{photoId}' does not belong to this menu.");
        }

        foreach (var photoId in photoIds)
            _photos.Remove(byId[photoId]);

        InputRevision++;

        return Result.Ok();
    }

    public void ApplyManualItem(
        Guid drinkDefinitionId,
        MenuItemAvailability availability,
        decimal? price,
        int? volumeMl,
        Guid? updatedByUserId)
    {
        var existing = _items.FirstOrDefault(i => i.DrinkDefinitionId == drinkDefinitionId);
        if (existing is null)
            _items.Add(ShopMenuItem.Create(drinkDefinitionId, availability, price, volumeMl, MenuItemSource.Manual));
        else
            existing.ApplyManual(availability, price, volumeMl);

        MarkManualUpdate(updatedByUserId);
    }

    public void ApplyManualItems(IReadOnlyList<ManualShopMenuItemUpdate> updates, Guid? updatedByUserId)
    {
        ManualShopMenuItemUpdateValidator.Validate(updates);

        foreach (var update in updates)
        {
            var existing = _items.FirstOrDefault(item => item.DrinkDefinitionId == update.DrinkDefinitionId);
            if (existing is null)
                _items.Add(ShopMenuItem.Create(update.DrinkDefinitionId, update.Availability,
                    update.Price, update.VolumeMl, MenuItemSource.Manual));
            else
                existing.ApplyManual(update.Availability, update.Price, update.VolumeMl);
        }
        MarkManualUpdate(updatedByUserId);
    }

    public void MarkManualUpdate(Guid? updatedByUserId)
    {
        InputRevision++;
        UpdatedByUserId = updatedByUserId;
        if (ParseStatus == MenuParseStatus.None)
            ParseStatus = MenuParseStatus.Ready;
    }

    public MenuOcrJob QueueOcrJob(IReadOnlyCollection<ShopMenuPhoto> inputPhotos, DateTime nowUtc)
    {
        var job = MenuOcrJob.CreatePublished(this, inputPhotos, nowUtc);
        _ocrJobs.Add(job);
        ActiveOcrJobId = job.Id;
        MarkParsePending(nowUtc);
        return job;
    }
}
