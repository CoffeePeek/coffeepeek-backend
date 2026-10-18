namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantReadState(TimeProvider clock)
{
    public CheckInStorageState Lease { get; } = new(clock);
}
