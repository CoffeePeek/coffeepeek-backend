namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record CheckInPhotoVariantDto(string Purpose, string Format, string ContentType,
    int Width, int Height, long SizeBytes, string SourceVersion, int RecipeVersion, string Url);
