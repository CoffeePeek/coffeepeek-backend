namespace CoffeePeek.Shops.Application.Features.CheckIn;

public sealed class CheckInPhotoDeliveryOptions
{
    public bool AdvertiseVariants { get; set; }
    public bool AdvertiseCanaryOnly { get; set; }
    public string AdvertiseCanaryUserIds { get; set; } = "";
    // Keep serving previously advertised immutable URLs when pausing generation/advertisement.
    public bool ServeVariants { get; set; }
    // Enable only after gateway, client and clock-boundary acceptance checks.
    public bool CachePublicVariants { get; set; }

    public bool IsCanaryConfigurationValid() => !AdvertiseCanaryOnly || TryReadCanaryIds(out _);

    public bool ShouldAdvertiseTo(Guid? viewerId) => AdvertiseVariants
        && (!AdvertiseCanaryOnly || (viewerId is { } id && id != Guid.Empty
            && TryReadCanaryIds(out var ids) && ids.Contains(id)));

    private bool TryReadCanaryIds(out HashSet<Guid> ids)
    {
        ids = [];
        if (string.IsNullOrWhiteSpace(AdvertiseCanaryUserIds)) return false;
        var values = AdvertiseCanaryUserIds.Split(',', 101, StringSplitOptions.TrimEntries);
        if (values.Length > 100) return false;
        foreach (var value in values)
            if (!Guid.TryParse(value, out var id) || id == Guid.Empty || !ids.Add(id)) return false;
        return ids.Count > 0;
    }
}
