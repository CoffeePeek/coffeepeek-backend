namespace CoffeePeek.Shops.Application.Features.CheckIn;

public sealed class CheckInPhotoDeliveryOptions
{
    public bool AdvertiseVariants { get; set; }
    // Keep serving previously advertised immutable URLs when pausing generation/advertisement.
    public bool ServeVariants { get; set; }
    // Enable only after gateway, client and clock-boundary acceptance checks.
    public bool CachePublicVariants { get; set; }
}
