using CoffeePeek.Moderation.Domain.Aggregates.Enums;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;

public sealed class MenuDraft
{
    public const int MaxPhotos = 4;
    public const string DefaultCurrency = "BYN";
    public const int MaxParseErrorLength = 1000;

    public DateTime? CapturedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public string Currency { get; private set; } = DefaultCurrency;
    public int ParseStatus { get; private set; }
    public string? ParseError { get; private set; }
    public Guid? ParseRequestId { get; private set; }
    public int? SuggestedPriceRange { get; private set; }
    private readonly List<MenuDraftItem> _items = [];
    private readonly List<MenuDraftPhoto> _photos = [];
    private readonly List<MenuDraftUnmatched> _unmatched = [];
    public IReadOnlyList<MenuDraftItem> Items => _items.AsReadOnly();
    public IReadOnlyList<MenuDraftPhoto> Photos => _photos.AsReadOnly();
    public IReadOnlyList<MenuDraftUnmatched> Unmatched => _unmatched.AsReadOnly();

    public static MenuDraft CreateEmpty() =>
        new()
        {
            Currency = DefaultCurrency,
            ParseStatus = (int)MenuDraftParseStatus.None
        };

    public void AttachPhotos(
        IReadOnlyList<(string FileName, string ContentType, string StorageKey, long SizeBytes)> photos,
        DateTime utcNow)
    {
        if (photos.Count == 0 || photos.Any(p => string.IsNullOrWhiteSpace(p.FileName)
                || string.IsNullOrWhiteSpace(p.ContentType) || string.IsNullOrWhiteSpace(p.StorageKey) || p.SizeBytes <= 0))
            throw new DomainException("Menu photos require valid upload metadata.");
        if (Photos.Select(p => p.StorageKey).Concat(photos.Select(p => p.StorageKey)).Distinct().Count() > MaxPhotos)
            throw new DomainException($"A menu can contain at most {MaxPhotos} photos.");
        foreach (var photo in photos)
        {
            if (Photos.Any(p => p.StorageKey == photo.StorageKey))
                continue;

            _photos.Add(new MenuDraftPhoto
            {
                Id = Guid.NewGuid(),
                FileName = photo.FileName,
                ContentType = photo.ContentType,
                StorageKey = photo.StorageKey,
                SizeBytes = photo.SizeBytes
            });
        }

        CapturedAtUtc ??= utcNow;
        UpdatedAtUtc = utcNow;
        ParseStatus = (int)MenuDraftParseStatus.Pending;
        ParseRequestId = Guid.NewGuid();
        ParseError = null;
    }

    public void MarkParsePending()
    {
        if (Photos.Count == 0)
            throw new DomainException("Attach menu photos before parsing.");
        ParseStatus = (int)MenuDraftParseStatus.Pending;
        ParseRequestId = Guid.NewGuid();
        ParseError = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public bool ApplyParseResult(
        bool success,
        string? error,
        int? suggestedPriceRange,
        IReadOnlyList<MenuDraftItem> items,
        IReadOnlyList<MenuDraftUnmatched> unmatched,
        DateTime utcNow,
        Guid? requestId = null)
    {
        // A manual edit, retry, or newer request invalidates results from an older parse.
        if (ParseRequestId != requestId || ParseStatus != (int)MenuDraftParseStatus.Pending)
            return false;
        if (success)
        {
            ValidateItems(items);
            if (suggestedPriceRange.HasValue && !Enum.IsDefined((PriceRange)suggestedPriceRange.Value))
                throw new DomainException("Unknown suggested price range.");
        }
        UpdatedAtUtc = utcNow;
        if (!success)
        {
            ParseStatus = (int)MenuDraftParseStatus.Failed;
            ParseError = ClipError(error);
            return true;
        }

        _items.RemoveAll(i => i.Source != (int)MenuDraftItemSource.Manual);
        _items.AddRange(items.Where(parsed => !_items.Any(manual =>
            string.Equals(manual.Slug.Trim(), parsed.Slug.Trim(), StringComparison.OrdinalIgnoreCase))));
        _unmatched.Clear();
        _unmatched.AddRange(unmatched);
        SuggestedPriceRange = suggestedPriceRange;
        ParseStatus = (int)MenuDraftParseStatus.Ready;
        ParseError = null;
        return true;
    }

    public void ApplyManualItems(IReadOnlyList<MenuDraftItem> edits, DateTime utcNow)
    {
        ValidateItems(edits);
        foreach (var edit in edits)
        {
            var existing = Items.FirstOrDefault(i =>
                string.Equals(i.Slug.Trim(), edit.Slug.Trim(), StringComparison.OrdinalIgnoreCase));
            var replacement = new MenuDraftItem
            {
                Slug = edit.Slug.Trim(),
                Availability = edit.Availability,
                Price = edit.Availability == (int)MenuDraftAvailability.Present ? edit.Price : null,
                VolumeMl = edit.VolumeMl,
                Source = (int)MenuDraftItemSource.Manual,
                NameRu = string.IsNullOrWhiteSpace(edit.NameRu) ? existing?.NameRu ?? "" : edit.NameRu,
                NameEn = string.IsNullOrWhiteSpace(edit.NameEn) ? existing?.NameEn ?? "" : edit.NameEn,
                Category = edit.Category == 0 ? existing?.Category ?? 0 : edit.Category
            };
            if (existing is null)
                _items.Add(replacement);
            else
                _items[_items.IndexOf(existing)] = replacement;
        }

        UpdatedAtUtc = utcNow;
        ParseRequestId = null;
        ParseStatus = (int)MenuDraftParseStatus.Ready;
        ParseError = null;
    }

    private static void ValidateItems(IReadOnlyList<MenuDraftItem> items)
    {
        if (items.Any(i => string.IsNullOrWhiteSpace(i.Slug) || !Enum.IsDefined((MenuDraftAvailability)i.Availability)
                           || i.Price < 0 || i.VolumeMl <= 0))
            throw new DomainException("Invalid menu item.");
        if (items.Select(i => i.Slug.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new DomainException("Menu item slugs must be unique.");
    }

    private static string? ClipError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return error;
        return error.Length <= MaxParseErrorLength ? error : error[..MaxParseErrorLength];
    }
}
