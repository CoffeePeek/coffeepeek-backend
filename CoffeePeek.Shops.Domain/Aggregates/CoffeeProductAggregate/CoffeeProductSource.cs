using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProductSource : Entity<Guid>
{
    public Guid CoffeeProductId { get; set; }
    public CoffeeProduct Product { get; set; } = null!;
    public string SourceKey { get; set; } = null!;
    public string ExternalProductId { get; set; } = null!;
    public string SourceUrl { get; set; } = null!;
    public string? SellerName { get; set; }
    public string? SourceBrand { get; set; }
    public string RawJson { get; set; } = "{}";
    private string[] _reviewWarnings = [];
    public string[] ReviewWarnings { get => [.. _reviewWarnings]; private set => _reviewWarnings = [.. value]; }
    public DateTime LastSeenAtUtc { get; set; }
    public bool IsPresent { get; set; } = true;
    private readonly List<CoffeeVariant> _variants = [];
    public IReadOnlyCollection<CoffeeVariant> Variants => _variants.AsReadOnly();
    private readonly List<CoffeeProductPhoto> _photos = [];
    public IReadOnlyCollection<CoffeeProductPhoto> Photos => _photos.AsReadOnly();
    public CoffeeProductSource() { Id = Guid.NewGuid(); }

    public void AttachVariant(CoffeeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (_variants.Any(existing => existing.Id == variant.Id ||
            existing.ExternalVariantId == variant.ExternalVariantId))
            throw new DomainException("Duplicate coffee variant identity.");
        variant.Source = this;
        variant.CoffeeProductSourceId = Id;
        _variants.Add(variant);
        Product.AdvanceVersion();
    }

    public void AttachPhoto(CoffeeProductPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (_photos.Any(existing => existing.Id == photo.Id))
            throw new DomainException("Duplicate coffee photo identity.");
        photo.CoffeeProductSourceId = Id;
        _photos.Add(photo);
        Product.AdvanceVersion();
    }

    public IReadOnlyList<CoffeeProductPhoto> RemovePhotosExcept(IReadOnlySet<string> storageKeys)
    {
        ArgumentNullException.ThrowIfNull(storageKeys);
        var removed = _photos.Where(photo => !storageKeys.Contains(photo.StorageKey)).ToArray();
        if (removed.Length == 0)
            return removed;
        _photos.RemoveAll(photo => !storageKeys.Contains(photo.StorageKey));
        Product.AdvanceVersion();
        return removed;
    }

    public void SetReviewWarnings(IEnumerable<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        ReviewWarnings = warnings.Distinct(StringComparer.Ordinal).ToArray();
        Product.AdvanceVersion();
    }
}
