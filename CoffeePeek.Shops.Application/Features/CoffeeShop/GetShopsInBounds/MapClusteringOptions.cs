namespace CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;

public sealed class MapClusteringOptions
{
    public const string SectionName = "MapClusteringOptions";

    public int ClusterMaxZoom { get; set; } = 10;
    public int ZoneMaxZoom { get; set; } = 13;
    public int ClusterCellPixels { get; set; } = 60;
    public int MaxResponseItems { get; set; } = 500;
}
