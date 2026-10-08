using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

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
