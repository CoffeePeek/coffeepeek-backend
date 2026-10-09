using CoffeePeek.Contract.Events;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class FaultMediaDeliveryMiddleware
{
    public static void After(ShopPhotosAttachedEvent message)
    {
        var path = ShopsFaultControl.Marker(ShopsFaultControl.Required("CP_FAULT_ROOT"), "media-handled");
        var count = File.Exists(path) && int.TryParse(File.ReadAllText(path), out var previous) ? previous : 0;
        File.WriteAllText(path, (count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
