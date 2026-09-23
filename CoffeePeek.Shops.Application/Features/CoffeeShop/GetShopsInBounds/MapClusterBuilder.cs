using CoffeePeek.Contract.Dtos.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;

public sealed record MapClusterPoint(Guid Id, decimal Latitude, decimal Longitude);

public static class MapClusterBuilder
{
    public static MapClusterDto[] Build(
        IEnumerable<MapClusterPoint> source,
        int zoom,
        int cellPixels)
    {
        var points = source.OrderBy(p => p.Id).ToArray();
        if (points.Length == 0)
            return [];

        var worldSize = 256d * Math.Pow(2d, zoom);
        return points
            .Select(point =>
            {
                var x = ((double)point.Longitude + 180d) / 360d * worldSize;
                var latitudeRadians = Math.Clamp((double)point.Latitude, -85.05112878d, 85.05112878d) * Math.PI / 180d;
                var y = (1d - Math.Asinh(Math.Tan(latitudeRadians)) / Math.PI) / 2d * worldSize;
                return new { Point = point, CellX = (long)Math.Floor(x / cellPixels), CellY = (long)Math.Floor(y / cellPixels) };
            })
            .GroupBy(x => (x.CellX, x.CellY))
            .OrderBy(g => g.Key.CellX)
            .ThenBy(g => g.Key.CellY)
            .Select(group => new MapClusterDto(
                $"{zoom}:{group.Key.CellX}:{group.Key.CellY}",
                group.Average(x => x.Point.Latitude),
                group.Average(x => x.Point.Longitude),
                group.Count(),
                new MapBoundsDto(
                    group.Min(x => x.Point.Latitude),
                    group.Min(x => x.Point.Longitude),
                    group.Max(x => x.Point.Latitude),
                    group.Max(x => x.Point.Longitude))))
            .ToArray();
    }
}
