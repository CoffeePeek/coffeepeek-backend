using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public static class MenuOcrPhotoSet
{
    public static string Serialize(IEnumerable<ShopMenuPhoto> photos) =>
        JsonSerializer.Serialize(photos.Select(MenuOcrPhotoSnapshot.From).ToArray());

    public static string Hash(IEnumerable<ShopMenuPhoto> photos)
    {
        // Sorting makes the identity independent of EF collection order. Storage keys are immutable media identities.
        var canonical = photos.Select(MenuOcrPhotoSnapshot.From)
            .OrderBy(p => p.StorageKey, StringComparer.Ordinal)
            .ThenBy(p => p.MediaPhotoId)
            .ThenBy(p => p.FileName, StringComparer.Ordinal)
            .ThenBy(p => p.ContentType, StringComparer.Ordinal)
            .ThenBy(p => p.SizeBytes)
            .Select(p => JsonSerializer.Serialize(p));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", canonical))));
    }
}
