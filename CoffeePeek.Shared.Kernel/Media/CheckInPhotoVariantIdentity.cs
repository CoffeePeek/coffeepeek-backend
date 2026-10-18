using System.Security.Cryptography;
using System.Text;

namespace CoffeePeek.Shared.Kernel.Media;

public static class CheckInPhotoVariantIdentity
{
    public static string SourceVersion(Guid photoId, string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"checkin:{photoId:N}:{key}")));

    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool IsAddress(string source, int recipe, string purpose, string format) =>
        IsDigest(source) && recipe > 0 && purpose is "thumb" or "feed" or "detail"
        && format is "webp" or "jpeg" or "png";

    public static string Url(Guid checkInId, Guid photoId, string source, int recipe, string purpose, string format) =>
        $"/api/v1/check-ins/{checkInId}/photos/{photoId}/variants/{source}/{recipe}/{purpose}.{format}";

    public static string ETag(string source, int recipe, string digest) => $"\"{source}-v{recipe}-{digest}\"";
}
