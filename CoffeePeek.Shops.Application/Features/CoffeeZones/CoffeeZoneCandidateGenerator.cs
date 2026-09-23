using CoffeePeek.Shops.Domain;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public sealed record CoffeeZoneCandidatePoint(Guid Id, decimal Latitude, decimal Longitude);

public static class CoffeeZoneCandidateGenerator
{
    public static CoffeeZoneCandidateDto[] Generate(
        IEnumerable<CoffeeZoneCandidatePoint> source,
        int radiusMeters,
        int minShops)
    {
        if (radiusMeters is < BusinessConstants.MinCoffeeZoneRadiusMeters or > BusinessConstants.MaxCoffeeZoneRadiusMeters)
            throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        if (minShops is < 3 or > 50)
            throw new ArgumentOutOfRangeException(nameof(minShops));

        var points = source.OrderBy(p => p.Id).ToArray();
        if (points.Length < minShops)
            return [];

        return Dbscan(points, radiusMeters, minShops)
            .Select(cluster => BuildBoundedCandidate(cluster, minShops))
            .OfType<CoffeeZoneCandidateDto>()
            .OrderByDescending(c => c.ShopCount)
            .ThenBy(c => c.CenterLatitude)
            .ThenBy(c => c.CenterLongitude)
            .ToArray();
    }

    private static CoffeeZoneCandidateDto? BuildBoundedCandidate(
        IReadOnlyCollection<CoffeeZoneCandidatePoint> cluster,
        int minShops)
    {
        var bounded = cluster.ToArray();

        // DBSCAN clusters may form long chains. A zone is a circle with a hard 2 km
        // domain limit, so never return shop ids that the proposed circle cannot contain.
        while (true)
        {
            var candidateLatitude = bounded.Average(p => p.Latitude);
            var candidateLongitude = bounded.Average(p => p.Longitude);
            var filtered = bounded
                .Where(p => GeoDistance.HaversineMeters(
                    candidateLatitude, candidateLongitude, p.Latitude, p.Longitude)
                    <= BusinessConstants.MaxCoffeeZoneRadiusMeters)
                .ToArray();

            if (filtered.Length < minShops)
                return null;
            if (filtered.Length == bounded.Length)
                break;

            bounded = filtered;
        }

        var latitude = bounded.Average(p => p.Latitude);
        var longitude = bounded.Average(p => p.Longitude);
        var requiredRadius = bounded.Max(p => GeoDistance.HaversineMeters(
            latitude, longitude, p.Latitude, p.Longitude));
        var suggestedRadius = Math.Clamp(
            (int)Math.Ceiling(requiredRadius / 25d) * 25,
            BusinessConstants.MinCoffeeZoneRadiusMeters,
            BusinessConstants.MaxCoffeeZoneRadiusMeters);

        return new CoffeeZoneCandidateDto(
            latitude, longitude, suggestedRadius, bounded.Length,
            bounded.Select(p => p.Id).Order().ToArray());
    }

    private static List<List<CoffeeZoneCandidatePoint>> Dbscan(
        CoffeeZoneCandidatePoint[] points,
        int radiusMeters,
        int minShops)
    {
        var meanLatitudeRadians = (double)points.Average(p => p.Latitude) * Math.PI / 180d;
        var longitudeMetersPerDegree = Math.Max(1d, 111_320d * Math.Cos(meanLatitudeRadians));
        var projected = points.Select(p => new ProjectedPoint(
            p, (double)p.Longitude * longitudeMetersPerDegree, (double)p.Latitude * 110_574d)).ToArray();
        var grid = projected
            .GroupBy(p => ((long)Math.Floor(p.X / radiusMeters), (long)Math.Floor(p.Y / radiusMeters)))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var visited = new HashSet<Guid>();
        var assigned = new HashSet<Guid>();
        var clusters = new List<List<CoffeeZoneCandidatePoint>>();

        ProjectedPoint[] Neighbours(ProjectedPoint point)
        {
            var cellX = (long)Math.Floor(point.X / radiusMeters);
            var cellY = (long)Math.Floor(point.Y / radiusMeters);
            var result = new List<ProjectedPoint>();
            for (var x = cellX - 1; x <= cellX + 1; x++)
            for (var y = cellY - 1; y <= cellY + 1; y++)
            {
                if (!grid.TryGetValue((x, y), out var candidates))
                    continue;
                result.AddRange(candidates.Where(candidate => GeoDistance.HaversineMeters(
                    point.Shop.Latitude, point.Shop.Longitude,
                    candidate.Shop.Latitude, candidate.Shop.Longitude) <= radiusMeters));
            }
            return result.OrderBy(p => p.Shop.Id).ToArray();
        }

        foreach (var point in projected)
        {
            if (!visited.Add(point.Shop.Id))
                continue;
            var neighbours = Neighbours(point);
            if (neighbours.Length < minShops)
                continue;

            var cluster = new List<CoffeeZoneCandidatePoint>();
            var queue = new Queue<ProjectedPoint>(neighbours);
            var queued = neighbours.Select(n => n.Shop.Id).ToHashSet();
            while (queue.TryDequeue(out var current))
            {
                if (visited.Add(current.Shop.Id))
                {
                    var currentNeighbours = Neighbours(current);
                    if (currentNeighbours.Length >= minShops)
                    {
                        foreach (var neighbour in currentNeighbours)
                            if (queued.Add(neighbour.Shop.Id))
                                queue.Enqueue(neighbour);
                    }
                }
                if (assigned.Add(current.Shop.Id))
                    cluster.Add(current.Shop);
            }
            clusters.Add(cluster.OrderBy(p => p.Id).ToList());
        }

        return clusters;
    }

    private sealed record ProjectedPoint(CoffeeZoneCandidatePoint Shop, double X, double Y);
}
