namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_000d;

    public static double HaversineMeters(decimal latitude1, decimal longitude1, decimal latitude2, decimal longitude2)
    {
        var lat1 = DegreesToRadians((double)latitude1);
        var lat2 = DegreesToRadians((double)latitude2);
        var deltaLat = lat2 - lat1;
        var deltaLon = DegreesToRadians((double)(longitude2 - longitude1));
        var a = Math.Pow(Math.Sin(deltaLat / 2d), 2d)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLon / 2d), 2d);
        return EarthRadiusMeters * 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
