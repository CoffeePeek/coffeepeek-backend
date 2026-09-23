namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

public sealed record CoffeeZonePoint(decimal Latitude, decimal Longitude)
{
    /// <summary>
    /// Regular polygon with vertices on a circle. 111 320 m/° is slightly above the haversine
    /// 111 195 m/°, so vertices land just inside <paramref name="radiusMeters"/> and the zone's
    /// bounding radius never exceeds it. The AddCoffeeZonePolygon migration uses the same math.
    /// </summary>
    public static CoffeeZonePoint[] Circle(decimal latitude, decimal longitude, int radiusMeters, int points = 32)
    {
        var latitudeDegrees = radiusMeters / 111_320d;
        var longitudeDegrees = latitudeDegrees / Math.Cos((double)latitude * Math.PI / 180d);
        return Enumerable.Range(0, points).Select(i =>
        {
            var angle = 2d * Math.PI * i / points;
            return new CoffeeZonePoint(
                Math.Round(latitude + (decimal)(latitudeDegrees * Math.Sin(angle)), BusinessConstants.MaxLocationScale),
                Math.Round(longitude + (decimal)(longitudeDegrees * Math.Cos(angle)), BusinessConstants.MaxLocationScale));
        }).ToArray();
    }

    public static bool PolygonContains(IReadOnlyList<CoffeeZonePoint> polygon, decimal latitude, decimal longitude)
    {
        // ponytail: planar ray casting on lat/lon; fine for zones up to 2 km, breaks across the antimeridian.
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if (a.Latitude > latitude != b.Latitude > latitude
                && longitude < (b.Longitude - a.Longitude) * (latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
                inside = !inside;
        }
        return inside;
    }
}
