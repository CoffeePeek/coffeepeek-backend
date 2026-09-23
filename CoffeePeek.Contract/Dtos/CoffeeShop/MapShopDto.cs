using CoffeePeek.Contract.Enums;
using System.Text.Json.Serialization;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public class MapShopDto
{
    public Guid Id { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string Title { get; set; } = string.Empty;
    public CoffeeShopType? Type { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? PrimaryZoneId { get; set; }
}
