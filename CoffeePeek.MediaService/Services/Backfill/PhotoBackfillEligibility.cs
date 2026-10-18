using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.Shared.Kernel.Media;

namespace CoffeePeek.MediaService.Services.Backfill;

public static class PhotoBackfillEligibility
{
    public static string? Evaluate(PhotoBackfillCandidate candidate, PhotoMetadata? photo, bool tombstoned)
    {
        if (candidate.PhotoId == Guid.Empty || candidate.CheckInId == Guid.Empty || candidate.OwnerId == Guid.Empty
            || candidate.Kind is not (CheckInKind.Shop or CheckInKind.Home)
            || string.IsNullOrWhiteSpace(candidate.StorageKey) || candidate.StorageKey.Length > 255
            || candidate.SourceVersion != CheckInPhotoVariantIdentity.SourceVersion(candidate.PhotoId, candidate.StorageKey))
            return "invalid_candidate";
        if (tombstoned) return "deleted";
        if (photo is null) return "media_missing";
        if (photo.Id != candidate.PhotoId || photo.OwnerId != candidate.OwnerId || photo.CheckInId != candidate.CheckInId
            || photo.StorageKey != candidate.StorageKey || photo.ShopAttachmentId is not null)
            return "binding_mismatch";
        if (photo.Status != PhotoStatus.Confirmed) return "media_not_confirmed";
        if (photo.BucketType != BucketType.CheckIn) return "needs_transfer";
        // Presign addresses use an unrelated hyphenated UUID. Only the server-owned
        // claim-copy keyspace proves that another PUT URL cannot replace this source.
        var extension = Path.GetExtension(photo.StorageKey);
        if (extension.Length is < 2 or > 12 || extension.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c))
            || photo.StorageKey != $"check-ins/{photo.Id:N}{extension}")
            return "provenance_unverified";
        return null;
    }
}
