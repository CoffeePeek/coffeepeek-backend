using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicShopChangePayloadDto(
    string? Description = null, ShopChangeContactsDto? Contacts = null, ShopChangeGalleryDto? Photos = null,
    string[]? Tags = null, string[]? Roasters = null, string[]? Equipments = null,
    ShopChangeMenuDto? Menu = null, string[]? BrewMethods = null);
