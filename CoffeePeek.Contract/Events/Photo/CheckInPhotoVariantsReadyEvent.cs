using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Kernel.Media;

namespace CoffeePeek.Contract.Events.Photo;

public sealed record CheckInPhotoVariantsReadyEvent(Guid JobId, Guid CheckInId, Guid OwnerId, Guid PhotoId,
    string SourceVersion, int RecipeVersion, CheckInPhotoVariantInfo[] Variants)
{
    public bool IsValid() => JobId != Guid.Empty && CheckInId != Guid.Empty && OwnerId != Guid.Empty
        && PhotoId != Guid.Empty && CheckInPhotoVariantIdentity.IsDigest(SourceVersion) && RecipeVersion > 0
        && Variants is { Length: 6 } && Variants.All(v => v is not null
            && CheckInPhotoVariantIdentity.IsAddress(SourceVersion, RecipeVersion, v.Purpose, v.Format)
            && v.ContentType == "image/" + v.Format && CheckInPhotoVariantIdentity.IsDigest(v.Digest)
            && v.Width is > 0 and <= 1600 && v.Height is > 0 and <= 1600
            && v.SizeBytes is > 0 and <= 8 * 1024 * 1024)
        && new[] { "thumb", "feed", "detail" }.All(p =>
            Variants.Count(v => v.Purpose == p && v.Format == "webp") == 1
            && Variants.Count(v => v.Purpose == p && v.Format is "jpeg" or "png") == 1);
}
