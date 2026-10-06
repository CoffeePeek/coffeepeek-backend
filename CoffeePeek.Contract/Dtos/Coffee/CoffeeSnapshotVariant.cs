using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

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
