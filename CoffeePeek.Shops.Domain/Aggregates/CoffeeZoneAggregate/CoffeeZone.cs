using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

public sealed class CoffeeZone : Entity<Guid>
{
    public Guid CityId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal CenterLatitude { get; private set; }
    public decimal CenterLongitude { get; private set; }
    public int RadiusMeters { get; private set; }
    public CoffeeZoneStatus Status { get; private set; }

    private CoffeeZone()
    {
    }

    public CoffeeZone(
        Guid cityId,
        string name,
        string? description,
        decimal centerLatitude,
        decimal centerLongitude,
        int radiusMeters)
    {
        Id = Guid.NewGuid();
        Status = CoffeeZoneStatus.Draft;
        Update(cityId, name, description, centerLatitude, centerLongitude, radiusMeters);
    }

    public void Update(
        Guid cityId,
        string name,
        string? description,
        decimal centerLatitude,
        decimal centerLongitude,
        int radiusMeters)
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

        if (centerLatitude is < -BusinessConstants.MaxLocationLatitude or > BusinessConstants.MaxLocationLatitude)
            throw new DomainException("Invalid coffee zone latitude.");

        if (centerLongitude is < -BusinessConstants.MaxLocationLongitude or > BusinessConstants.MaxLocationLongitude)
            throw new DomainException("Invalid coffee zone longitude.");

        if (radiusMeters is < BusinessConstants.MinCoffeeZoneRadiusMeters or > BusinessConstants.MaxCoffeeZoneRadiusMeters)
            throw new DomainException(
                $"Coffee zone radius must be between {BusinessConstants.MinCoffeeZoneRadiusMeters} and {BusinessConstants.MaxCoffeeZoneRadiusMeters} meters.");

        CityId = cityId;
        Name = trimmedName;
        Description = trimmedDescription;
        CenterLatitude = centerLatitude;
        CenterLongitude = centerLongitude;
        RadiusMeters = radiusMeters;
    }

    public void Publish() => Status = CoffeeZoneStatus.Published;

    public void MoveToDraft() => Status = CoffeeZoneStatus.Draft;

    public void Archive() => Status = CoffeeZoneStatus.Archived;
}
