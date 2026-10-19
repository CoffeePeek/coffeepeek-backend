namespace CoffeePeek.Shops.Application.Features.CheckIn;

public sealed class CheckInPhotoDeliveryOptions
{
    public bool AdvertiseVariants { get; set; }
    // Keep serving previously advertised immutable URLs when pausing generation/advertisement.
    public bool ServeVariants { get; set; }
}
