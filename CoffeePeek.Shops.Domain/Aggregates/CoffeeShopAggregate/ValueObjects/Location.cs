using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public record Location
{
    public string Address { get; init; } = null!;
    public bool IsAddressValidated { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public Guid CityId { get; init; }

    private Location() { }

    public static Location CreateValidated(Guid cityId, string address, decimal lat, decimal lon)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new DomainException("Address cannot be empty.");

        if (lat is < -BusinessConstants.MaxLocationLatitude or > BusinessConstants.MaxLocationLatitude)
            throw new DomainException("Invalid Latitude.");

        if (lon is < -BusinessConstants.MaxLocationLongitude or > BusinessConstants.MaxLocationLongitude)
            throw new DomainException("Invalid Longitude.");

        return new Location
        {
            CityId = cityId,
            Address = address.Trim(),
            Latitude = lat,
            Longitude = lon,
            IsAddressValidated = true
        };
    }

    public static Location CreateDraft(Guid cityId, string address)
    {
        return new Location
        {
            CityId = cityId,
            Address = address.Trim(),
            IsAddressValidated = false
        };
    }
}