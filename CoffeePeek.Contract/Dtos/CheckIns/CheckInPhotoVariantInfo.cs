namespace CoffeePeek.Contract.Dtos.CheckIns;

/// <summary>Verified immutable metadata; private object keys never leave Media.</summary>
public sealed record CheckInPhotoVariantInfo(string Purpose, string Format, string ContentType,
    int Width, int Height, long SizeBytes, string Digest);
