namespace CoffeePeek.MediaService.Services.Variants;

public sealed record RenderedPhotoVariant(byte[] Bytes, string ContentType, int Width, int Height, bool HasAlpha);
