using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ShopChangeGalleryDto(
    IReadOnlyList<Guid> RetainedPhotoIds,
    IReadOnlyList<UploadedPhotoDto> NewPhotos);
