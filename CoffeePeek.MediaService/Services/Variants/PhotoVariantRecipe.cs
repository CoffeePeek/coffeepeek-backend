using CoffeePeek.Shared.Kernel.Media;

namespace CoffeePeek.MediaService.Services.Variants;

public static class PhotoVariantRecipe
{
    // Bump whenever presets, renderer version, format or quality changes.
    public const int Version = 1;
    public static readonly IReadOnlyDictionary<string, int> Sizes =
        new Dictionary<string, int> { ["thumb"] = 320, ["feed"] = 960, ["detail"] = 1600 }.AsReadOnly();

    public static string SourceVersion(Guid photoId, string key) =>
        CheckInPhotoVariantIdentity.SourceVersion(photoId, key);

    public static string Preset(string purpose, string format)
    {
        if (!Sizes.ContainsKey(purpose) || format is not ("webp" or "jpeg" or "png"))
            throw new ArgumentException("Unknown photo recipe.");
        return $"v{Version}_{purpose}_{format}";
    }

    public static string ObjectKey(Guid jobId, Guid leaseToken, string purpose, string format)
    {
        _ = Preset(purpose, format);
        return $"v{Version}/{jobId:N}/{leaseToken:N}/{purpose}.{format}";
    }
}
