using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

public sealed class CoffeeZone : Entity<Guid>
{
    public Guid CityId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    /// <summary>Centroid of <see cref="Polygon"/>; derived, used as the map label anchor.</summary>
    public decimal CenterLatitude { get; private set; }
    public decimal CenterLongitude { get; private set; }
    /// <summary>Bounding circle of <see cref="Polygon"/>; derived, used for viewport filtering and primary-zone ordering.</summary>
    public int RadiusMeters { get; private set; }
    public List<CoffeeZonePoint> Polygon { get; private set; } = [];
    public CoffeeZoneStatus Status { get; private set; }

    private CoffeeZone()
    {
    }

    public CoffeeZone(Guid cityId, string name, string? description, IReadOnlyCollection<CoffeeZonePoint> polygon)
    {
        Id = Guid.NewGuid();
        Status = CoffeeZoneStatus.Draft;
        Update(cityId, name, description, polygon);
    }

    public void Update(Guid cityId, string name, string? description, IReadOnlyCollection<CoffeeZonePoint> polygon)
    {
        if (CityId != Guid.Empty && CityId != cityId)
            throw new DomainException("A coffee zone cannot be moved to another city.");

        if (cityId == Guid.Empty)
            throw new DomainException("CityId is required.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Coffee zone name is required.");

        var trimmedName = name.Trim();
        if (trimmedName.Length > BusinessConstants.MaxCoffeeZoneNameLength)
            throw new DomainException($"Coffee zone name cannot exceed {BusinessConstants.MaxCoffeeZoneNameLength} characters.");

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > BusinessConstants.MaxCoffeeZoneDescriptionLength)
            throw new DomainException($"Coffee zone description cannot exceed {BusinessConstants.MaxCoffeeZoneDescriptionLength} characters.");

        var vertices = polygon?.ToList() ?? [];
        if (vertices.Count is < BusinessConstants.MinCoffeeZonePolygonPoints or > BusinessConstants.MaxCoffeeZonePolygonPoints)
            throw new DomainException(
                $"Coffee zone polygon must have between {BusinessConstants.MinCoffeeZonePolygonPoints} and {BusinessConstants.MaxCoffeeZonePolygonPoints} points.");
        if (vertices.Any(p => p is null
                              || p.Latitude is < -BusinessConstants.MaxLocationLatitude or > BusinessConstants.MaxLocationLatitude
                              || p.Longitude is < -BusinessConstants.MaxLocationLongitude or > BusinessConstants.MaxLocationLongitude))
            throw new DomainException("Invalid coffee zone polygon point.");

        // A circle around the vertex centroid that holds every vertex also holds the whole polygon.
        var centerLatitude = Math.Round(vertices.Average(p => p.Latitude), BusinessConstants.MaxLocationScale);
        var centerLongitude = Math.Round(vertices.Average(p => p.Longitude), BusinessConstants.MaxLocationScale);
        var boundingRadius = (int)Math.Ceiling(vertices.Max(p =>
            GeoDistance.HaversineMeters(centerLatitude, centerLongitude, p.Latitude, p.Longitude)));
        if (boundingRadius > BusinessConstants.MaxCoffeeZoneRadiusMeters)
            throw new DomainException(
                $"Coffee zone polygon must fit within {BusinessConstants.MaxCoffeeZoneRadiusMeters} meters of its center.");

        CityId = cityId;
        Name = trimmedName;
        Description = trimmedDescription;
        CenterLatitude = centerLatitude;
        CenterLongitude = centerLongitude;
        RadiusMeters = Math.Max(boundingRadius, BusinessConstants.MinCoffeeZoneRadiusMeters);
        Polygon = vertices;
    }

    public bool Contains(decimal latitude, decimal longitude) =>
        CoffeeZonePoint.PolygonContains(Polygon, latitude, longitude);

    public void Publish() => Status = CoffeeZoneStatus.Published;

    public void MoveToDraft() => Status = CoffeeZoneStatus.Draft;

    public void Archive() => Status = CoffeeZoneStatus.Archived;
}
