using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public record ModerationLocation
{
    public string Address { get; private set; } = null!;
    public bool IsAddressValidated { get; private set; }
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }

    public string Value => Address;

    // ReSharper disable once UnusedMember.Local
    private ModerationLocation()
    {
    }

    public ModerationLocation(string address)
    {
        ValidateAddress(address);
        Address = address;
        IsAddressValidated = false;
    }

    public ModerationLocation(string validatedAddress, decimal lat, decimal lon)
    {
        SetLocation(lat, lon, validatedAddress);
    }

    public void SetLocation(decimal lat, decimal lon, string validatedAddress)
    {
        ValidateAddress(validatedAddress);
        if (lat is < -90 or > 90 || lon is < -180 or > 180)
            throw new DomainException("Invalid geographic coordinates.");
        Latitude = lat;
        Longitude = lon;
        Address = validatedAddress;
        IsAddressValidated = true;
    }

    private static void ValidateAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new DomainException("Address is required.");
    }
}
