using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicModerationRoasterDto(Guid Id, string Name, string? About, ModerationStatus ModerationStatus,
    string? RejectedReason, PublicAddressDto? City, CoffeePeek.Contract.Dtos.Shop.LocationDto? Location,
    CoffeePeek.Contract.Dtos.Shop.RoasterContactDto? Contact, ShortPhotoMetadataDto[] Photos);
