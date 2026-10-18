namespace CoffeePeek.Shops.Application.Features.CheckIn;

public sealed record CheckInPhotoVisibilityFacts(Guid[] ExcludedUserIds, CheckInPhotoPolicyObservation Observation)
{
    // Unknown provenance stays conservative: private access requires a fresh Account recheck.
    public bool ReadFromSource { get; init; }
}
