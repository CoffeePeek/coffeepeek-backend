namespace CoffeePeek.Shops.Application.Features.CheckIn;

public sealed record CheckInPhotoVisibilityFacts(Guid[] ExcludedUserIds, CheckInPhotoPolicyObservation Observation);
