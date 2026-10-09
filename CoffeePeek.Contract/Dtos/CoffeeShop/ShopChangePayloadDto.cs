namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ShopChangePayloadDto(
    string? Description = null,
    ShopChangeContactsDto? Contacts = null,
    ShopChangeGalleryDto? Photos = null,
    IReadOnlyList<Guid>? TagIds = null,
    IReadOnlyList<Guid>? RoasterIds = null,
    IReadOnlyList<Guid>? EquipmentIds = null,
    ShopChangeMenuDto? Menu = null,
    IReadOnlyList<Guid>? BrewMethodIds = null);
