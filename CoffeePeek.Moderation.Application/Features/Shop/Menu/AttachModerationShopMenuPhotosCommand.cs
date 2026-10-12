using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Moderation.Application.Features.Shop.Menu;

public record AttachModerationShopMenuPhotosCommand(
    Guid ShopId,
    IReadOnlyList<UploadedPhotoDto> Photos,
    Guid UserId);
