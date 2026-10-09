using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProductSource : Entity<Guid>
{
    public Guid CoffeeProductId { get; private set; }
    public CoffeeProduct Product { get; private set; } = null!;
    public string SourceKey { get; private set; } = null!;
    public string ExternalProductId { get; private set; } = null!;
    public string SourceUrl { get; private set; } = null!;
    public string? SellerName { get; private set; }
    public string? SourceBrand { get; private set; }
    public string RawJson { get; private set; } = "{}";
    private string[] _reviewWarnings = [];
    public string[] ReviewWarnings { get => [.. _reviewWarnings]; private set => _reviewWarnings = [.. value]; }
    public DateTime LastSeenAtUtc { get; private set; }
    public bool IsPresent { get; private set; } = true;
    private readonly List<CoffeeVariant> _variants = [];
    public IReadOnlyCollection<CoffeeVariant> Variants => _variants.AsReadOnly();
    private readonly List<CoffeeProductPhoto> _photos = [];
    public IReadOnlyCollection<CoffeeProductPhoto> Photos => _photos.AsReadOnly();
    private CoffeeProductSource() { }
    public CoffeeProductSource(CoffeeProduct product, string sourceKey, string externalProductId, string sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(product);
        ValidateIdentity(sourceKey, 255);
        ValidateIdentity(externalProductId, 300);
        ValidateUrl(sourceUrl);
        Id = Guid.NewGuid(); Product = product; CoffeeProductId = product.Id;
        SourceKey = sourceKey; ExternalProductId = externalProductId; SourceUrl = sourceUrl;
    }

    public void UpdateMetadata(string externalProductId, string sourceUrl, string? brand, string? seller,
        string rawJson, DateTime collectedAtUtc)
    {
        ValidateIdentity(externalProductId, 300); ValidateUrl(sourceUrl);
        using var json = System.Text.Json.JsonDocument.Parse(rawJson);
        if (collectedAtUtc == default || collectedAtUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Source observation must have a UTC timestamp.");
        Product.AdvanceVersion();
        ExternalProductId = externalProductId; SourceUrl = sourceUrl; SourceBrand = brand; SellerName = seller;
        RawJson = rawJson; LastSeenAtUtc = collectedAtUtc; IsPresent = true;
    }

    public void MarkAvailability(CoffeeAvailability availability, DateTime? checkedAtUtc = null)
    {
        if (!Enum.IsDefined(availability) || checkedAtUtc is { } time && (time == default || time.Kind != DateTimeKind.Utc))
            throw new DomainException("Invalid source availability observation.");
        Product.AdvanceVersion();
        IsPresent = availability != CoffeeAvailability.Unknown;
        if (checkedAtUtc.HasValue) LastSeenAtUtc = checkedAtUtc.Value;
        foreach (var variant in _variants) variant.MarkAvailability(availability, checkedAtUtc);
    }

    public void AttachVariant(CoffeeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (!ReferenceEquals(variant.Source, this)) throw new DomainException("Variant belongs to a different source.");
        if (_variants.Any(existing => existing.Id == variant.Id ||
            existing.ExternalVariantId == variant.ExternalVariantId))
            throw new DomainException("Duplicate coffee variant identity.");
        _variants.Add(variant);
        Product.AdvanceVersion();
    }

    public void AttachPhoto(CoffeeProductPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (_photos.Any(existing => existing.Id == photo.Id || existing.StorageKey == photo.StorageKey))
            throw new DomainException("Duplicate coffee photo identity.");
        photo.AttachTo(Id);
        _photos.Add(photo);
        Product.AdvanceVersion();
    }

    public void UpdatePhoto(CoffeeProductPhoto photo, string sourceUrl, string fileName, string contentType,
        long sizeBytes, Guid mediaPhotoId, int sortIndex)
    {
        if (!_photos.Contains(photo)) throw new DomainException("Photo belongs to a different source.");
        photo.UpdateMetadata(sourceUrl, fileName, contentType, sizeBytes, mediaPhotoId, sortIndex);
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
        var values = warnings.Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length > 300 || values.Any(value => value is null || value.Length > 500))
            throw new DomainException("Invalid coffee review warnings.");
        ReviewWarnings = values;
        Product.AdvanceVersion();
    }

    internal static void ValidateIdentity(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new DomainException("Invalid coffee source identity.");
    }

    internal static void ValidateUrl(string value)
    {
        if (value is null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new DomainException("Invalid coffee source URL.");
    }
}
