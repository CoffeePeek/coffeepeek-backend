using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

/// <summary>Attaches a copy of the linked bag's card, the image a public post shows instead of the private shelf card.</summary>
internal static class HomeCheckInBagCard
{
    /// <summary>Returns the copy's storage key for <c>CheckInPhotosAttachedEvent</c>.</summary>
    public static async Task<string> AttachAsync(DomainCheckIn checkIn, Guid bagId, Guid cardPhotoId,
        IUploadedPhotoLookup photos, CancellationToken ct)
    {
        var copy = await photos.CopyShelfBagCardAsync(checkIn.UserId, checkIn.Id, bagId, cardPhotoId,
                CopyId(checkIn.Id, cardPhotoId, checkIn.ContentRevision), ct)
            ?? throw new ValidationException("The bag card could not be attached, please retry.");
        checkIn.AttachShelfBagPhoto(new CheckInPhoto(copy.FileName, copy.ContentType, copy.StorageKey, copy.SizeBytes,
            checkIn.UserId, checkIn.NextPhotoSortIndex(), copy.PhotoId));
        return copy.StorageKey;
    }

    /// <summary>
    /// A retried request sees the same stored revision and gets the same copy; showing the card again after hiding it
    /// happens at a later revision and gets a new copy, since the hidden one is deleted.
    /// </summary>
    private static Guid CopyId(Guid checkInId, Guid cardPhotoId, int revision) => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"shelf-bag-card:{checkInId:N}:{cardPhotoId:N}:{revision}")).AsSpan(0, 16));
}
