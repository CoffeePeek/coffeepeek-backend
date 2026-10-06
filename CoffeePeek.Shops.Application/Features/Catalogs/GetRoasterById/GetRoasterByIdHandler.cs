using System.Net;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;

public static class GetRoasterByIdHandler
{
    public static async Task<Response<RoasterDetailsDto>> Handle(
        GetRoasterByIdQuery query,
        IQueryRoasterRepository repository,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        ICoffeeCatalogStore coffees,
        CancellationToken ct)
    {
        var roaster = await repository.GetByIdWithShopsAndPhotosAsync(query.Id, ct);
        if (roaster is null)
            return Response<RoasterDetailsDto>.Error(HttpStatusCode.NotFound, "Roaster not found.");

        var counts = (await coffees.RoasterCountsAsync([roaster.Id], ct)).GetValueOrDefault(roaster.Id);
        return Response<RoasterDetailsDto>.Success(ToDto(roaster, mediaOptions.Value) with
        {
            CoffeeShopsCount = counts.CoffeeShopsCount,
            CoffeeProductsCount = counts.CoffeeProductsCount
        });
    }

    internal static RoasterDetailsDto ToDto(Roaster roaster, MediaPublicUrlOptions mediaOptions) => new()
    {
        Id = roaster.Id,
        Name = roaster.Name,
        About = roaster.About,
        CityId = roaster.Location?.CityId,
        Location = roaster.Location is null
            ? null
            : new LocationDto
            {
                Address = roaster.Location.Address,
                Latitude = roaster.Location.Latitude,
                Longitude = roaster.Location.Longitude
            },
        Contact = roaster.Contact is null
            ? null
            : new RoasterContactDto
            {
                InstagramLink = roaster.Contact.InstagramLink,
                SiteLink = roaster.Contact.SiteLink
            },
        Photos = roaster.Photos
            .OrderBy(p => p.SortIndex)
            .Select(p => new ShortPhotoMetadataDto
            {
                Id = p.Id,
                FileName = p.FileName,
                StorageKey = p.StorageKey,
                FullUrl = MediaStorageUrlBuilder.BuildPublicUrl(
                    mediaOptions.PublicEndpoint, mediaOptions.ShopBucketName, p.StorageKey) ?? string.Empty,
                Urls = PhotoUrlsDto.Create(mediaOptions, mediaOptions.ShopBucketName, p.StorageKey),
                SortIndex = p.SortIndex,
                IsPrimary = p.SortIndex == 0
            })
            .ToArray(),
        Shops = roaster.CoffeeShops
            .Where(s => !s.IsHidden)
            .Select(s =>
            {
                var coverPhoto = s.ShopPhotos
                    .OrderBy(p => p.SortIndex)
                    .ThenBy(p => p.CreatedAtUtc)
                    .FirstOrDefault();

                return new RoasterLinkedShopDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    CoverPhoto = coverPhoto is null
                        ? null
                        : new ShortPhotoMetadataDto
                        {
                            Id = coverPhoto.Id,
                            FileName = coverPhoto.FileName,
                            StorageKey = coverPhoto.StorageKey,
                            FullUrl = MediaStorageUrlBuilder.BuildPublicUrl(
                                mediaOptions.PublicEndpoint,
                                mediaOptions.ShopBucketName,
                                coverPhoto.StorageKey) ?? string.Empty,
                            Urls = PhotoUrlsDto.Create(mediaOptions, mediaOptions.ShopBucketName, coverPhoto.StorageKey),
                            SortIndex = coverPhoto.SortIndex,
                            IsPrimary = coverPhoto.SortIndex == 0
                        }
                };
            })
            .ToArray()
    };
}
