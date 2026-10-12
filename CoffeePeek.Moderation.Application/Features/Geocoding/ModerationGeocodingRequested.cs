using Wolverine.Attributes;

namespace CoffeePeek.Moderation.Application.Features.Geocoding;

// The submitted address is part of the request so a delayed delivery cannot geocode a newer edit.
[LocalQueue("moderation-geocoding")]
public sealed record ModerationGeocodingRequested(
    ModerationGeocodingKind Kind,
    Guid EntityId,
    string SubmittedAddress);
