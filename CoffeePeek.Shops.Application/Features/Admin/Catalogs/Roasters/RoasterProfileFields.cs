using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

internal static class RoasterProfileFields
{
    public static Location? BuildLocation(Guid? cityId, string? address, decimal? latitude, decimal? longitude)
    {
        if (cityId is null || string.IsNullOrWhiteSpace(address))
            return null;

        return latitude.HasValue && longitude.HasValue
            ? Location.CreateValidated(cityId.Value, address, latitude.Value, longitude.Value)
            : Location.CreateDraft(cityId.Value, address);
    }

    public static RoasterContact? BuildContact(string? instagramLink, string? siteLink)
    {
        if (string.IsNullOrWhiteSpace(instagramLink) && string.IsNullOrWhiteSpace(siteLink))
            return null;

        return RoasterContact.Create(instagramLink, siteLink);
    }

    public static Guid CreateId(Guid actorId, string[] keys) => keys.Length == 0 ? Guid.NewGuid()
        : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("roaster-upload:" + actorId.ToString("N") + ":"
            + JsonSerializer.Serialize(keys.OrderBy(k => k, StringComparer.Ordinal)))).AsSpan(0, 16));

    public static async Task<string[]> ReplacePhotosAsync(Roaster roaster, Guid actorId,
        IReadOnlyList<UploadedPhotoDto> input, IUploadedPhotoLookup lookup, CancellationToken ct)
    {
        if (input.Count == 0) { roaster.ReplacePhotos([]); return []; }
        TrustedUploadedShopPhotos.ValidateKeys(actorId, input);
        if (roaster.Photos.GroupBy(p => p.StorageKey, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ConflictException("Roaster gallery contains ambiguous legacy photos; reconcile it before replacement.");
        var existing = roaster.Photos.ToDictionary(p => p.StorageKey, StringComparer.Ordinal);
        var additions = input.Where(p => !existing.ContainsKey(p.StorageKey)).ToArray();
        var claims = additions.Length == 0 ? [] : await TrustedUploadedShopPhotos.ClaimAsync(
            lookup, actorId, roaster.Id, ShopPhotoAttachmentKind.Roaster, additions, ct);
        var added = claims.ToDictionary(p => p.StorageKey, p => new RoasterPhoto(p.FileName, p.ContentType,
            p.StorageKey, p.SizeBytes, actorId, id: p.PhotoId), StringComparer.Ordinal);
        roaster.ReplacePhotos(input.Select(p => existing.TryGetValue(p.StorageKey, out var retained)
            ? retained : added[p.StorageKey]));
        var index = 0;
        foreach (var photo in roaster.Photos) photo.SetSortIndex(index++);
        return claims.Select(p => p.StorageKey).ToArray();
    }
}
