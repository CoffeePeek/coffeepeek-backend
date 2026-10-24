namespace CoffeePeek.Shared.Kernel.Geocoding;

public interface IAddressGeocoder
{
    Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken = default);
}
