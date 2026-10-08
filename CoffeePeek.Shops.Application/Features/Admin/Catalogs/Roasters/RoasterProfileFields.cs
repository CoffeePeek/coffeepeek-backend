using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using MapsterMapper;
using CoffeePeek.Shops.Application.Abstractions;

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

    public static IEnumerable<RoasterPhoto> ToRoasterPhotos(IEnumerable<ResolvedUploadedPhotoDto> photos, Guid ownerId) =>
        photos.Select(p => new RoasterPhoto(p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, ownerId));
}
