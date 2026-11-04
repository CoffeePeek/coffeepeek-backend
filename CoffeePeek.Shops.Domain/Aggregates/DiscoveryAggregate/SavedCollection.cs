using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;

/// <summary>Editable owner library. One owner collection can carry the stable default identity.</summary>
public sealed class SavedCollection
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Mode => "list";
    public string Visibility { get; private set; } = "private";
    public bool IsDefault { get; private set; }
    public string? Description { get; private set; }
    public Guid? CoverPhotoId { get; private set; }
    public string? CoverStorageKey { get; private set; }
    public string? CoverContentType { get; private set; }
    public long Revision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public List<SavedCollectionItem> Items { get; private set; } = [];
    private SavedCollection() { }

    public void Revoke() => RevokedAtUtc ??= DateTime.UtcNow;

    public void MarkDefault() => IsDefault = true;

    public static SavedCollection CreateList(Guid ownerId, Guid id, string title, string? description, string visibility,
        SavedCollectionCover? cover = null, IEnumerable<DiscoveryFavorite>? favorites = null)
    {
        if (ownerId == Guid.Empty || id == Guid.Empty) throw new DomainException("Invalid collection owner or identity.");
        var collection = new SavedCollection { Id = id, OwnerId = ownerId, CreatedAtUtc = DateTime.UtcNow };
        collection.Edit(title, description, visibility, cover, false);
        if (favorites != null) collection.ReplaceItems(favorites);
        collection.Revision = 1;
        return collection;
    }

    public void Edit(string title, string? description, string visibility, SavedCollectionCover? cover, bool removeCover)
    {
        EnsureList();
        ValidateDetails(title, description, visibility);
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Visibility = visibility;
        if (cover != null || removeCover)
        {
            CoverPhotoId = cover?.PhotoId;
            CoverStorageKey = cover?.StorageKey;
            CoverContentType = cover?.ContentType;
        }
        Touch();
    }

    public void ReplaceItems(IEnumerable<DiscoveryFavorite> favorites)
    {
        EnsureList();
        var selected = favorites.ToArray();
        if (selected.Any(f => f.UserId != OwnerId || f.Kind is not ("coffee_shop" or "roaster")) ||
            selected.Select(f => (f.Kind, f.EntityId)).Distinct().Count() != selected.Length)
            throw new DomainException("Invalid collection membership.");
        var existing = Items.ToDictionary(i => (i.Kind, i.EntityId));
        Items = selected.Select((favorite, position) =>
        {
            var item = existing.GetValueOrDefault((favorite.Kind, favorite.EntityId))
                ?? new SavedCollectionItem(Id, position, favorite.Kind, favorite.EntityId);
            item.Move(position);
            return item;
        }).ToList();
        Touch();
    }

    public static void ValidateDetails(string title, string? description, string visibility)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 100 || title.Any(char.IsControl) ||
            description?.Length > 1000 || description?.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) == true ||
            visibility is not ("public" or "private"))
            throw new DomainException("Invalid collection details.");
    }

    private void EnsureList()
    {
        if (Mode != "list" || RevokedAtUtc != null) throw new DomainException("This collection cannot be edited.");
    }

    private void Touch() { Revision++; UpdatedAtUtc = DateTime.UtcNow; }
}
