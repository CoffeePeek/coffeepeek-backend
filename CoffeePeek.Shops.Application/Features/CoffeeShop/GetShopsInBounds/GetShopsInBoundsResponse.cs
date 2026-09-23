using CoffeePeek.Contract.Dtos.CoffeeShop;
using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;

public class GetShopsInBoundsResponse(IEnumerable<MapShopDto> shops)
{
    public MapShopDto[] Shops { get; set; } = shops.ToArray();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MapClusterDto[]? Clusters { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MapCoffeeZoneDto[]? Zones { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsTruncated { get; init; }
}
