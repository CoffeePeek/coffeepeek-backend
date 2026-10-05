using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CountryDto(string Code, string NameRu, string NameEn);
public sealed record RoasterTagDto(Guid Id, string Slug, string Name, string? Description, int SortOrder, bool IsActive);
public sealed record RoasterTagEdit(string Name, string? Description, int SortOrder, bool IsActive = true);
public sealed record CreateRoasterTagRequest(string Slug, string Name, string? Description, int SortOrder = 0);
public sealed record SetRoasterTagsRequest(Guid[] TagIds);
public sealed record CoffeeContentDto(
    string Name, string? Description, string ProductKind, string ProductForm, string CompositionKind,
    string? Processing, string? RoastLevel, string? Acidity, string? Body, string? QGraderScoreRaw,
    string[] TasteDescriptors, string[] BrewRecommendations, string[] GrindOptions, string[] Features);
public sealed record CoffeeEditRequest(CoffeeContentDto Content, string[] CountryCodes,
    string[] ProtectedFields, string Status, long Version);
public sealed record CreateCoffeeRequest(Guid RoasterId, CoffeeContentDto Content, string[] CountryCodes);
public sealed record CoffeeVariantDto(int? WeightGrams, decimal Price, string Currency, string? Grind,
    string? RoastPurpose, string Availability, string AvailabilityScope, string SellerName,
    string SourceUrl, DateTime CheckedAtUtc);
public sealed record CoffeeProductDto(PublicAddressDto Address, PublicRoasterSummaryDto Roaster,
    CoffeeContentDto Content, CountryDto[] Countries, ShortPhotoMetadataDto[] Photos,
    CoffeeVariantDto[] Variants, DateTime? UpdatedAtUtc);
public sealed record AdminCoffeeDto(Guid Id, Guid RoasterId, string? Slug, string Status,
    CoffeeContentDto Content, CountryDto[] Countries, string[] ProtectedFields,
    ShortPhotoMetadataDto[] Photos, CoffeeVariantDto[] Variants, string[] ReviewWarnings, long Version);
public sealed record CoffeeImportResult(Guid RunId, string SourceKey, string SnapshotId,
    string Status, int Products, int Variants, int Added, bool MissingAvailabilityApplied, DateTime AppliedAtUtc);
public sealed record CoffeeImportRunDto(Guid Id, string SourceKey, string SnapshotId, string Status,
    DateTime CollectedAtUtc, DateTime AppliedAtUtc, int Products, int Variants, int Added,
    bool MissingAvailabilityApplied, string? Error);
public sealed record CoffeeImportFailureDto(string SourceKey, string SnapshotId, DateTimeOffset CollectedAtUtc,
    string Stage, string Error);
public sealed record CoffeeRoasterPageDto(PublicRoasterSummaryDto Roaster, PublicTagDto[] Tags,
    int AvailableProducts, DateTime? CatalogUpdatedAtUtc);
public sealed record CoffeeRoasterOverviewDto(PublicTagDto[] Tags, int AvailableProducts, DateTime? CatalogUpdatedAtUtc);

public sealed class CoffeeCatalogFilter
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? RoasterSlug { get; set; }
    public string? Search { get; set; }
    public string[] Countries { get; set; } = [];
    public string? ProductKind { get; set; }
    public string? Processing { get; set; }
    public string? Brew { get; set; }
    public int? WeightGrams { get; set; }
    public string? Currency { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool AvailableOnly { get; set; } = true;
}

// Wire contract for the existing Python snapshots; unrelated/raw source fields are retained in SourceProperties.
public sealed class CoffeeSnapshotImportRequest
{
    public string SourceKey { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
    public CoffeeSnapshotDto Snapshot { get; set; } = new();
}
public sealed class CoffeeSnapshotDto
{
    public int SchemaVersion { get; set; }
    public string AdapterVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CollectedAtUtc { get; set; }
    public bool Complete { get; set; }
    public CoffeeSnapshotProduct[] Products { get; set; } = [];
    public CoffeeSnapshotCoverage[] Coverage { get; set; } = [];
    public CoffeeSnapshotExcluded[] Excluded { get; set; } = [];
}
public sealed class CoffeeSnapshotCoverage
{
    public int? ExpectedProducts { get; set; }
    public int? DiscoveredProducts { get; set; }
}
public sealed class CoffeeSnapshotExcluded
{
    public string? SourceProductId { get; set; }
    public string? Reason { get; set; }
    public CoffeeSnapshotVariant[] Variants { get; set; } = [];
}
public sealed class CoffeeSnapshotProduct
{
    public string SourceKey { get; set; } = string.Empty;
    public string SourceProductId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RoasterName { get; set; }
    public string? SellerName { get; set; }
    public string ProductKind { get; set; } = string.Empty;
    public string ProductForm { get; set; } = string.Empty;
    public string CompositionKind { get; set; } = "unknown";
    public string AvailabilityScope { get; set; } = "online";
    public CoffeeSnapshotOrigin[] Origins { get; set; } = [];
    public string? Processing { get; set; }
    public string? RoastLevel { get; set; }
    public string? Acidity { get; set; }
    public string? Body { get; set; }
    public string? QGraderScoreRaw { get; set; }
    public string[] TasteDescriptors { get; set; } = [];
    public string[] BrewRecommendations { get; set; } = [];
    public string[] GrindOptions { get; set; } = [];
    public string[] Features { get; set; } = [];
    public string[] ImageUrls { get; set; } = [];
    public StoredCoffeePhotoDto[] StoredPhotos { get; set; } = [];
    public CoffeeSnapshotVariant[] Variants { get; set; } = [];
    public JsonElement SourceProperties { get; set; } = JsonSerializer.SerializeToElement(new { });
    public string[] ReviewWarnings { get; set; } = [];
}
public sealed class CoffeeSnapshotOrigin
{
    public string CountryCode { get; set; } = string.Empty;
    public string? EvidenceField { get; set; }
}
public sealed class CoffeeSnapshotVariant
{
    public string ExternalVariantId { get; set; } = string.Empty;
    public int? WeightGrams { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool AvailableForPurchase { get; set; }
    public JsonElement SourceAttributes { get; set; } = JsonSerializer.SerializeToElement(new { });
}
public sealed record StoredCoffeePhotoDto(Guid Id, string StorageKey, string FileName,
    string ContentType, long SizeBytes, string? SourceUrl = null);
