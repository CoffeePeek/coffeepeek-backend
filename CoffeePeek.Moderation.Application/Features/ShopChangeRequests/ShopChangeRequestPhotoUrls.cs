using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

/// <summary>Resolves photo previews on response DTOs without changing the stored request payload.</summary>
public static class ShopChangeRequestPhotoUrls
{
    public static ShopChangeRequestDto Enrich(ShopChangeRequestDto request, MediaPublicUrlOptions options)
    {
        UploadedPhotoDto Resolve(UploadedPhotoDto photo) => photo with
        {
            FullUrl = MediaStorageUrlBuilder.BuildPublicUrl(
                options.PublicEndpoint, options.ShopBucketName, photo.StorageKey)
        };

        var payload = request.Payload;
        return request with
        {
            Payload = payload with
            {
                Photos = payload.Photos is { } gallery
                    ? gallery with { NewPhotos = gallery.NewPhotos.Select(Resolve).ToArray() }
                    : null,
                Menu = payload.Menu is { } menu
                    ? menu with { NewPhotos = menu.NewPhotos.Select(Resolve).ToArray() }
                    : null
            }
        };
    }
}
