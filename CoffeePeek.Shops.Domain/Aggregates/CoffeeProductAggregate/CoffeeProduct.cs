using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public enum CoffeePublicationStatus { Draft, Published, Archived }
public enum CoffeeAvailability { Unknown, InStock, OutOfStock }

public sealed record CoffeeContent(
    string Name, string? Description, string ProductKind, string ProductForm, string CompositionKind,
    string? Processing, string? RoastLevel, string? Acidity, string? Body, string? QGraderScoreRaw,
    string[] TasteDescriptors, string[] BrewRecommendations, string[] GrindOptions, string[] Features);

public sealed class CoffeeProduct : Entity<Guid>, IPublicSlugOwner
{
    public Guid RoasterId { get; private set; }
    public string? PublicSlug { get; private set; }
    public long SlugRevision { get; private set; }
    public long Version { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string ProductKind { get; private set; } = null!;
    public string ProductForm { get; private set; } = null!;
    public string CompositionKind { get; private set; } = null!;
    public string? Processing { get; private set; }
    public string? RoastLevel { get; private set; }
    public string? Acidity { get; private set; }
    public string? Body { get; private set; }
    public string? QGraderScoreRaw { get; private set; }
    public string[] TasteDescriptors { get; private set; } = [];
    public string[] BrewRecommendations { get; private set; } = [];
    public string[] GrindOptions { get; private set; } = [];
    public string[] Features { get; private set; } = [];
    public string[] ProtectedFields { get; private set; } = [];
    public CoffeePublicationStatus Status { get; private set; }
    public List<CoffeeProductCountry> Countries { get; private set; } = [];
    public List<CoffeeProductSource> Sources { get; private set; } = [];

    private CoffeeProduct() { }
    public CoffeeProduct(Guid roasterId, CoffeeContent content)
    {
        if (roasterId == Guid.Empty) throw new DomainException("Roaster is required.");
        Id = Guid.NewGuid();
        RoasterId = roasterId;
        ApplyContent(content);
    }

    public void SetPublicSlug(string slug)
    {
        CoffeePeek.Shared.Domain.PublicAddresses.PublicSlug.Validate(slug);
        if (PublicSlug == slug) return;
        PublicSlug = slug;
        SlugRevision = checked(SlugRevision + 1);
    }

    public bool IsProtected(string field) => ProtectedFields.Contains(field, StringComparer.Ordinal);
    public void ProtectFields(string[] fields)
    {
        var allowed = new[] { nameof(Name), nameof(Description), nameof(ProductKind), nameof(ProductForm),
            nameof(CompositionKind), nameof(Processing), nameof(RoastLevel), nameof(Acidity), nameof(Body),
            nameof(QGraderScoreRaw), nameof(TasteDescriptors), nameof(BrewRecommendations), nameof(GrindOptions),
            nameof(Features), nameof(Countries), "Photos" };
        if (fields.Any(f => !allowed.Contains(f, StringComparer.Ordinal)))
            throw new DomainException("Unknown protected coffee field.");
        ProtectedFields = fields.Distinct(StringComparer.Ordinal).ToArray();
        Version = checked(Version + 1);
    }

    public void ApplyContent(CoffeeContent data, bool fromImport = false)
    {
        if (string.IsNullOrWhiteSpace(data.Name) || data.Name.Trim().Length > 250)
            throw new DomainException("Coffee name must contain 1–250 characters.");
        if (data.Description?.Length > 20000) throw new DomainException("Coffee description is too long.");
        if (data.ProductKind is not ("roasted_beans" or "green_beans") ||
            data.ProductForm is not ("whole_beans" or "ground_only") ||
            data.CompositionKind is not ("unknown" or "single_origin" or "blend"))
            throw new DomainException("Invalid coffee kind, form or composition.");
        bool Write(string field) => !fromImport || !IsProtected(field);
        if (Write(nameof(Name))) Name = data.Name.Trim();
        if (Write(nameof(Description)) && (!fromImport || data.Description is not null)) Description = data.Description;
        if (Write(nameof(ProductKind))) ProductKind = data.ProductKind;
        if (Write(nameof(ProductForm))) ProductForm = data.ProductForm;
        if (Write(nameof(CompositionKind))) CompositionKind = data.CompositionKind;
        if (Write(nameof(Processing)) && (!fromImport || data.Processing is not null)) Processing = data.Processing;
        if (Write(nameof(RoastLevel)) && (!fromImport || data.RoastLevel is not null)) RoastLevel = data.RoastLevel;
        if (Write(nameof(Acidity)) && (!fromImport || data.Acidity is not null)) Acidity = data.Acidity;
        if (Write(nameof(Body)) && (!fromImport || data.Body is not null)) Body = data.Body;
        if (Write(nameof(QGraderScoreRaw)) && (!fromImport || data.QGraderScoreRaw is not null)) QGraderScoreRaw = data.QGraderScoreRaw;
        if (Write(nameof(TasteDescriptors)) && (!fromImport || data.TasteDescriptors.Length > 0)) TasteDescriptors = data.TasteDescriptors;
        if (Write(nameof(BrewRecommendations)) && (!fromImport || data.BrewRecommendations.Length > 0)) BrewRecommendations = data.BrewRecommendations;
        if (Write(nameof(GrindOptions)) && (!fromImport || data.GrindOptions.Length > 0)) GrindOptions = data.GrindOptions;
        if (Write(nameof(Features)) && (!fromImport || data.Features.Length > 0)) Features = data.Features;
        Version = checked(Version + 1);
    }

    public void SetStatus(CoffeePublicationStatus status)
    {
        if (!Enum.IsDefined(status)) throw new DomainException("Invalid publication status.");
        Status = status;
        Version = checked(Version + 1);
    }
    public void ReplaceCountries(IEnumerable<string> codes)
    {
        var values = codes.Distinct(StringComparer.Ordinal).ToArray();
        if (values.Any(c => c.Length != 2 || c.Any(x => x is < 'A' or > 'Z')))
            throw new DomainException("Country codes must be ISO alpha-2 codes.");
        Countries.RemoveAll(c => !values.Contains(c.CountryCode));
        foreach (var code in values.Except(Countries.Select(c => c.CountryCode)))
            Countries.Add(new CoffeeProductCountry(Id, code));
        Version = checked(Version + 1);
    }
}

public sealed class Country
{
    public string Code { get; private set; } = null!;
    public string NameRu { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    private Country() { }
    public Country(string code, string nameRu, string nameEn) { Code = code; NameRu = nameRu; NameEn = nameEn; }
}

public sealed class CoffeeProductCountry
{
    public Guid CoffeeProductId { get; private set; }
    public string CountryCode { get; private set; } = null!;
    public Country Country { get; private set; } = null!;
    private CoffeeProductCountry() { }
    public CoffeeProductCountry(Guid productId, string code) { CoffeeProductId = productId; CountryCode = code; }
}

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
    public string[] ReviewWarnings { get; set; } = [];
    public DateTime LastSeenAtUtc { get; set; }
    public bool IsPresent { get; set; } = true;
    public List<CoffeeVariant> Variants { get; set; } = [];
    public List<CoffeeProductPhoto> Photos { get; set; } = [];
    public CoffeeProductSource() { Id = Guid.NewGuid(); }
}

public sealed class CoffeeVariant : Entity<Guid>
{
    public Guid CoffeeProductSourceId { get; set; }
    public CoffeeProductSource Source { get; set; } = null!;
    public string SourceKey { get; set; } = null!;
    public string ExternalVariantId { get; set; } = null!;
    public int? WeightGrams { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = null!;
    public string? Grind { get; set; }
    public string? RoastPurpose { get; set; }
    public string SourceAttributesJson { get; set; } = "{}";
    public CoffeeAvailability Availability { get; set; }
    public string AvailabilityScope { get; set; } = "online";
    public DateTime CheckedAtUtc { get; set; }
    public CoffeeVariant() { Id = Guid.NewGuid(); }
}

public sealed class CoffeeProductPhoto : Entity<Guid>
{
    public Guid CoffeeProductSourceId { get; set; }
    public string SourceUrl { get; set; } = null!;
    public string StorageKey { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public Guid MediaPhotoId { get; set; }
    public int SortIndex { get; set; }
    public CoffeeProductPhoto() { Id = Guid.NewGuid(); }
}

public sealed class CoffeeImportRun : Entity<Guid>
{
    public string SourceKey { get; set; } = null!;
    public string SnapshotId { get; set; } = null!;
    public string PayloadHash { get; set; } = null!;
    public DateTime CollectedAtUtc { get; set; }
    public DateTime AppliedAtUtc { get; set; }
    public string Status { get; set; } = "Applied";
    public int Products { get; set; }
    public int Variants { get; set; }
    public int Added { get; set; }
    public bool MissingAvailabilityApplied { get; set; }
    public string? Error { get; set; }
    public CoffeeImportRun() { Id = Guid.NewGuid(); }
}
