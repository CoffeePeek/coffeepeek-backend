using System.Text.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

internal static class CheckInPhotoVariantBatch
{
    // One SQL batch per already-authorized page, including the separate drink cutout.
    public static async Task<Dictionary<Guid, CheckInPhotoVariantDto[]>> ReadAsync(ShopsDbContext db,
        IEnumerable<(Guid CheckInId, CheckInPhotoDto Photo)> photos, bool enabled, CancellationToken ct)
    {
        if (!enabled) return [];
        var requested = photos.DistinctBy(p => p.Photo.Id).ToDictionary(p => p.Photo.Id);
        if (requested.Count == 0) return [];
        // Page limits are 100 (+1 for pagination) and at most six attached photos per check-in.
        if (requested.Count > 606) throw new InvalidOperationException("Photo manifest batch exceeded page limit.");
        var ids = requested.Keys.ToArray();
        var sources = requested.Values.Select(p => CheckInPhotoVariantIdentity.SourceVersion(p.Photo.Id, p.Photo.StorageKey)).ToArray();
        var manifests = await db.CheckInPhotoVariantManifests.AsNoTracking()
            .Where(m => ids.Contains(m.PhotoId) && sources.Contains(m.SourceVersion) && !db.CheckInPhotoVariantManifests.Any(newer =>
                newer.PhotoId == m.PhotoId && newer.SourceVersion == m.SourceVersion && newer.RecipeVersion > m.RecipeVersion))
            .ToArrayAsync(ct);
        var result = new Dictionary<Guid, CheckInPhotoVariantDto[]>();
        foreach (var m in manifests)
        {
            var (checkInId, photo) = requested[m.PhotoId];
            if (m.CheckInId != checkInId || m.SourceVersion != CheckInPhotoVariantIdentity.SourceVersion(photo.Id, photo.StorageKey)) continue;
            var variants = JsonSerializer.Deserialize<CheckInPhotoVariantInfo[]>(m.VariantsJson)!;
            result[m.PhotoId] = variants.Select(v => new CheckInPhotoVariantDto(v.Purpose, v.Format, v.ContentType,
                v.Width, v.Height, v.SizeBytes, m.SourceVersion, m.RecipeVersion,
                CheckInPhotoVariantIdentity.Url(checkInId, photo.Id, m.SourceVersion, m.RecipeVersion, v.Purpose, v.Format))).ToArray();
        }
        return result;
    }

    public static IEnumerable<CheckInPhotoDto> Photos(CheckInPhotoDto[] photos, CheckInPhotoDto? drink,
        CheckInPhotoDto? shelfBag = null) =>
        photos.Concat(new[] { drink, shelfBag }.OfType<CheckInPhotoDto>());

    public static CheckInPhotoDto WithVariants(CheckInPhotoDto photo, Dictionary<Guid, CheckInPhotoVariantDto[]> variants) =>
        photo with { Variants = variants.GetValueOrDefault(photo.Id) };
}
