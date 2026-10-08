using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Menu;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ShopChangeMenuDto(
    IReadOnlyList<UpdateShopMenuItemRequest> Items,
    IReadOnlyList<Guid> RetainedPhotoIds,
    IReadOnlyList<UploadedPhotoDto> NewPhotos,
    long? ExpectedInputRevision = null);
