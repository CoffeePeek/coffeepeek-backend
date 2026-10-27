namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

public sealed partial class CheckIn
{
    public double? DeviceLatitude { get; private set; }
    public double? DeviceLongitude { get; private set; }
    public double? LocationAccuracyMeters { get; private set; }
    public DateTime? LocationCapturedAtUtc { get; private set; }
    public double? VerifiedShopLatitude { get; private set; }
    public double? VerifiedShopLongitude { get; private set; }
    public double? ShopDistanceMeters { get; private set; }
    public string LeaderboardGeoStatus { get; private set; } = "MissingLocation";
    public bool? LeaderboardOverride { get; private set; }
    public DateTime? LeaderboardCreditedAtUtc { get; private set; }
    public long LeaderboardVersion { get; private set; } = 1;

    public bool CountsForLeaderboard => Kind == CheckInKind.Shop && IsPubliclyVisible
        && (LeaderboardOverride ?? LeaderboardGeoStatus == "WithinRange");

    public string LeaderboardStatus => Kind == CheckInKind.Home ? "HomeCheckIn"
        : DeletedAtUtc != null ? "Deleted"
        : Visibility != CheckInVisibility.Public ? "Private"
        : ModerationState != CheckInModerationState.Approved ? "AwaitingApproval"
        : LeaderboardOverride == false ? "ExcludedByAdministrator"
        : CountsForLeaderboard ? "Counted" : LeaderboardGeoStatus;

    public void VerifyLocation(double latitude, double longitude, double accuracyMeters, DateTime capturedAtUtc,
        double? shopLatitude, double? shopLongitude, DateTime nowUtc)
    {
        if (DeviceLatitude.HasValue || ContentRevision != 1 || ModerationDecisionSequence != 0)
            throw new CoffeePeek.Shared.Kernel.Exceptions.DomainException("Check-in location can only be recorded on creation.");
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || !double.IsFinite(accuracyMeters)
            || latitude is < -90 or > 90 || longitude is < -180 or > 180 || accuracyMeters <= 0
            || capturedAtUtc == default || capturedAtUtc.Kind != DateTimeKind.Utc)
        {
            LeaderboardGeoStatus = "InvalidLocation";
            return;
        }
        DeviceLatitude = latitude;
        DeviceLongitude = longitude;
        LocationAccuracyMeters = accuracyMeters;
        LocationCapturedAtUtc = capturedAtUtc;
        VerifiedShopLatitude = shopLatitude;
        VerifiedShopLongitude = shopLongitude;
        LeaderboardGeoStatus = "MissingShopLocation";
        if (shopLatitude is not { } lat || shopLongitude is not { } lon
            || !double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90 || lon is < -180 or > 180)
            return;

        const double radians = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat - latitude) * radians / 2), 2)
            + Math.Cos(latitude * radians) * Math.Cos(lat * radians) * Math.Pow(Math.Sin((lon - longitude) * radians / 2), 2);
        ShopDistanceMeters = 6371008.8 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
        LeaderboardGeoStatus = accuracyMeters > 50 ? "InaccurateLocation"
            : capturedAtUtc < nowUtc.AddMinutes(-5) || capturedAtUtc > nowUtc.AddMinutes(1) ? "StaleLocation"
            : ShopDistanceMeters <= 50 ? "WithinRange" : "OutsideRadius";
    }
}
